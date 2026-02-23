using System.ComponentModel.DataAnnotations;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Admin;
using EJCFitnessGym.Models;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Services.Identity;
using EJCFitnessGym.Services.Realtime;
using EJCFitnessGym.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
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

    public IReadOnlyList<SelectListItem> AvailablePlans { get; private set; } = Array.Empty<SelectListItem>();

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

        [Display(Name = "Membership plan")]
        public int? SubscriptionPlanId { get; set; }

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

    public async Task OnGetAsync(string? returnUrl = null, int? planId = null)
    {
        ReturnUrl = AccountFlowHelper.NormalizeMemberReturnUrl(Url, returnUrl);
        Input.SubscriptionPlanId = planId > 0 ? planId : null;
        ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
        await LoadPlanOptionsAsync(Input.SubscriptionPlanId);
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
        Input.SubscriptionPlanId = Input.SubscriptionPlanId > 0 ? Input.SubscriptionPlanId : null;

        SubscriptionPlan? selectedPlan = null;
        if (Input.SubscriptionPlanId.HasValue)
        {
            selectedPlan = await _db.SubscriptionPlans
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == Input.SubscriptionPlanId.Value && p.IsActive);

            if (selectedPlan is null)
            {
                ModelState.AddModelError(nameof(Input.SubscriptionPlanId), "Selected membership plan is unavailable.");
            }
        }

        await LoadPlanOptionsAsync(Input.SubscriptionPlanId);

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

            var branchClaimResult = await _userManager.AddClaimAsync(
                user,
                new Claim(BranchAccess.BranchIdClaimType, registrationBranchId));
            if (!branchClaimResult.Succeeded)
            {
                foreach (var error in branchClaimResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                await transaction.RollbackAsync();
                return Page();
            }

            _db.MemberProfiles.Add(new MemberProfile
            {
                UserId = user.Id,
                FirstName = Input.FirstName,
                LastName = Input.LastName,
                PhoneNumber = Input.PhoneNumber,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            });

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
                    phoneNumber = Input.PhoneNumber,
                    selectedPlanId = selectedPlan?.Id,
                    selectedPlanName = selectedPlan?.Name
                });

            var postRegistrationReturnUrl = BuildPostRegistrationReturnUrl(returnUrl, selectedPlan?.Id);

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

    private async Task LoadPlanOptionsAsync(int? selectedPlanId)
    {
        var planOptions = await _db.SubscriptionPlans
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Price)
            .ThenBy(p => p.Name)
            .Select(p => new SelectListItem
            {
                Value = p.Id.ToString(),
                Text = $"{p.Name} (PHP {p.Price:0.00} • {p.BillingCycle})"
            })
            .ToListAsync();

        if (selectedPlanId.HasValue && planOptions.All(p => p.Value != selectedPlanId.Value.ToString()))
        {
            Input.SubscriptionPlanId = null;
        }

        AvailablePlans = planOptions;
    }

    private string BuildPostRegistrationReturnUrl(string fallbackReturnUrl, int? selectedPlanId)
    {
        if (selectedPlanId.HasValue)
        {
            var membershipReturnUrl = Url.Page("/Public/Pricing", values: new { planId = selectedPlanId.Value });
            if (!string.IsNullOrWhiteSpace(membershipReturnUrl))
            {
                return membershipReturnUrl;
            }
        }

        return AccountFlowHelper.NormalizeMemberReturnUrl(Url, fallbackReturnUrl);
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

        const string bootstrapBranchId = "BR-CENTRAL";
        const string bootstrapBranchName = "EJC Central Branch";

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
