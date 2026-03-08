using System.ComponentModel.DataAnnotations;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Admin;
using EJCFitnessGym.Models;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Services.Identity;
using EJCFitnessGym.Services.Memberships;
using EJCFitnessGym.Services.Realtime;
using EJCFitnessGym.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;

#nullable enable

namespace EJCFitnessGym.Areas.Identity.Pages.Account;

public class RegisterModel : PageModel
{
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly IUserStore<IdentityUser> _userStore;
    private readonly ILogger<RegisterModel> _logger;
    private readonly IEmailVerificationCodeService _emailVerificationCodeService;
    private readonly IConfiguration _configuration;
    private readonly ApplicationDbContext _db;
    private readonly IErpEventPublisher _erpEventPublisher;

    public RegisterModel(
        UserManager<IdentityUser> userManager,
        IUserStore<IdentityUser> userStore,
        SignInManager<IdentityUser> signInManager,
        ILogger<RegisterModel> logger,
        IEmailVerificationCodeService emailVerificationCodeService,
        IConfiguration configuration,
        ApplicationDbContext db,
        IErpEventPublisher erpEventPublisher)
    {
        _userManager = userManager;
        _userStore = userStore;
        _signInManager = signInManager;
        _logger = logger;
        _emailVerificationCodeService = emailVerificationCodeService;
        _configuration = configuration;
        _db = db;
        _erpEventPublisher = erpEventPublisher;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string ReturnUrl { get; set; } = string.Empty;

    public IList<AuthenticationScheme> ExternalLogins { get; set; } = new List<AuthenticationScheme>();

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        [Display(Name = "First name")]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        [Display(Name = "Last name")]
        public string LastName { get; set; } = string.Empty;

        [Phone]
        [StringLength(30)]
        [Display(Name = "Phone number")]
        public string? PhoneNumber { get; set; }

        [Required]
        [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public async Task OnGetAsync(string? returnUrl = null, int? planId = null, string? googleError = null)
    {
        ReturnUrl = AccountFlowHelper.NormalizeMemberReturnUrl(Url, returnUrl);
        if (planId > 0 && string.IsNullOrEmpty(returnUrl))
        {
            // If they came from a plan link, ensure they go back to pricing to pay after registration.
            ReturnUrl = Url.Page("/Public/Pricing", new { planId }) ?? ReturnUrl;
        }

        if (!string.IsNullOrWhiteSpace(googleError))
        {
            ModelState.AddModelError(string.Empty, googleError);
        }

        ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl = AccountFlowHelper.NormalizeMemberReturnUrl(Url, returnUrl);
        ReturnUrl = returnUrl;
        ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
        Input.Email = (Input.Email ?? string.Empty).Trim().ToLowerInvariant();
        Input.FirstName = (Input.FirstName ?? string.Empty).Trim();
        Input.LastName = (Input.LastName ?? string.Empty).Trim();
        Input.PhoneNumber = string.IsNullOrWhiteSpace(Input.PhoneNumber) ? null : Input.PhoneNumber.Trim();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var existingUser = await _userManager.FindByEmailAsync(Input.Email);
        if (existingUser is not null)
        {
            ModelState.AddModelError(nameof(Input.Email), "A user with this email already exists.");
            return Page();
        }

        var user = CreateUser();
        await _userStore.SetUserNameAsync(user, Input.Email, CancellationToken.None);
        var emailStore = GetEmailStore();
        await emailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);
        user.PhoneNumber = Input.PhoneNumber;

        await using var transaction = await _db.Database.BeginTransactionAsync(CancellationToken.None);

        var result = await _userManager.CreateAsync(user, Input.Password);
        if (result.Succeeded)
        {
            _logger.LogInformation("User created a new account with password.");

            var roleResult = await _userManager.AddToRoleAsync(user, "Member");
            if (!roleResult.Succeeded)
            {
                foreach (var error in roleResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                await transaction.RollbackAsync();
                return Page();
            }

            var registrationBranchId = await ResolveRegistrationBranchIdAsync(CancellationToken.None);
            if (string.IsNullOrWhiteSpace(registrationBranchId))
            {
                ModelState.AddModelError(string.Empty, "No branch is configured for new member registration. Please contact support.");
                await transaction.RollbackAsync();
                return Page();
            }

            var profile = new MemberProfile
            {
                UserId = user.Id,
                FirstName = Input.FirstName,
                LastName = Input.LastName,
                PhoneNumber = Input.PhoneNumber,
                HomeBranchId = registrationBranchId,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };
            _db.MemberProfiles.Add(profile);

            try
            {
                await MemberBranchAssignment.AssignHomeBranchAsync(
                    _db,
                    _userManager,
                    user,
                    registrationBranchId,
                    profile);
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                await transaction.RollbackAsync();
                return Page();
            }

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            await _erpEventPublisher.PublishToBackOfficeAsync(
                "member.registered",
                "New member registration completed.",
                new
                {
                    userId = user.Id,
                    email = user.Email,
                    firstName = Input.FirstName,
                    lastName = Input.LastName,
                    phoneNumber = Input.PhoneNumber
                });

            var postRegistrationReturnUrl = returnUrl;

            var requiresConfirmedAccount = _userManager.Options.SignIn.RequireConfirmedAccount;
            if (requiresConfirmedAccount)
            {
                try
                {
                    await _emailVerificationCodeService.SendVerificationCodeAsync(user);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send verification code to {Email}.", Input.Email);
                    TempData["StatusMessage"] = "Account created, but we couldn't send the verification code yet. Use resend verification code after SMTP is configured.";
                    return RedirectToPage("RegisterConfirmation", new { email = Input.Email, returnUrl = postRegistrationReturnUrl });
                }
            }

            if (requiresConfirmedAccount)
            {
                return RedirectToPage("RegisterConfirmation", new { email = Input.Email, returnUrl = postRegistrationReturnUrl });
            }

            await _signInManager.SignInAsync(user, isPersistent: false);
            return LocalRedirect(postRegistrationReturnUrl);
        }

        await transaction.RollbackAsync();

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }

        return Page();
    }


    private async Task<string?> ResolveRegistrationBranchIdAsync(CancellationToken cancellationToken)
    {
        var configuredBranchId = _configuration["BranchAccess:DefaultBranchId"]?.Trim();
        if (!string.IsNullOrWhiteSpace(configuredBranchId))
        {
            return configuredBranchId;
        }

        var activeBranchId = await _db.BranchRecords
            .AsNoTracking()
            .Where(branch => branch.IsActive)
            .OrderBy(branch => branch.BranchId)
            .Select(branch => branch.BranchId)
            .FirstOrDefaultAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(activeBranchId))
        {
            return activeBranchId;
        }

        var fallbackBranchId = await _db.BranchRecords
            .AsNoTracking()
            .OrderBy(branch => branch.BranchId)
            .Select(branch => branch.BranchId)
            .FirstOrDefaultAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(fallbackBranchId))
        {
            return fallbackBranchId.Trim();
        }

        var existingClaimBranchId = await _db.UserClaims
            .AsNoTracking()
            .Where(claim =>
                claim.ClaimType == BranchAccess.BranchIdClaimType &&
                claim.ClaimValue != null)
            .OrderByDescending(claim => claim.Id)
            .Select(claim => claim.ClaimValue)
            .FirstOrDefaultAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(existingClaimBranchId))
        {
            return existingClaimBranchId.Trim();
        }

        const string bootstrapBranchId = BranchNaming.DefaultBranchId;
        const string bootstrapBranchName = BranchNaming.DefaultLocationName;

        try
        {
            _db.BranchRecords.Add(new BranchRecord
            {
                BranchId = bootstrapBranchId,
                Name = bootstrapBranchName,
                IsActive = true,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(cancellationToken);
            return bootstrapBranchId;
        }
        catch (DbUpdateException)
        {
            var seededBranchId = await _db.BranchRecords
                .AsNoTracking()
                .Where(branch => branch.BranchId == bootstrapBranchId)
                .Select(branch => branch.BranchId)
                .FirstOrDefaultAsync(cancellationToken);

            return string.IsNullOrWhiteSpace(seededBranchId) ? null : seededBranchId.Trim();
        }
    }

    private IdentityUser CreateUser()
    {
        try
        {
            return Activator.CreateInstance<IdentityUser>();
        }
        catch
        {
            throw new InvalidOperationException($"Cannot create an instance of '{nameof(IdentityUser)}'.");
        }
    }

    private IUserEmailStore<IdentityUser> GetEmailStore()
    {
        if (!_userManager.SupportsUserEmail)
        {
            throw new NotSupportedException("The default UI requires a user store with email support.");
        }

        return (IUserEmailStore<IdentityUser>)_userStore;
    }
}
