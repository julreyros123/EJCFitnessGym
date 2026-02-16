using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Services.Realtime;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

#nullable enable

namespace EJCFitnessGym.Areas.Identity.Pages.Account;

public class RegisterModel : PageModel
{
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly IUserStore<IdentityUser> _userStore;
    private readonly ILogger<RegisterModel> _logger;
    private readonly IEmailSender _emailSender;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly ApplicationDbContext _db;
    private readonly IErpEventPublisher _erpEventPublisher;

    public RegisterModel(
        UserManager<IdentityUser> userManager,
        IUserStore<IdentityUser> userStore,
        SignInManager<IdentityUser> signInManager,
        ILogger<RegisterModel> logger,
        IEmailSender emailSender,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ApplicationDbContext db,
        IErpEventPublisher erpEventPublisher)
    {
        _userManager = userManager;
        _userStore = userStore;
        _signInManager = signInManager;
        _logger = logger;
        _emailSender = emailSender;
        _configuration = configuration;
        _environment = environment;
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

            var userId = await _userManager.GetUserIdAsync(user);
            var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            var postRegistrationReturnUrl = BuildPostRegistrationReturnUrl(returnUrl, selectedPlan?.Id);

            var callbackUrl = AccountFlowHelper.BuildAbsolutePageUrl(
                Url,
                Request,
                _configuration,
                "/Account/ConfirmEmail",
                new { area = "Identity", userId, code, returnUrl = postRegistrationReturnUrl });

            if (!string.IsNullOrEmpty(callbackUrl))
            {
                try
                {
                    await _emailSender.SendEmailAsync(
                        Input.Email,
                        "Confirm your email",
                        $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send confirmation email to {Email}.", Input.Email);

                    // In development, SMTP is often not configured yet. Don't block sign-up.
                    if (!_environment.IsDevelopment() && _userManager.Options.SignIn.RequireConfirmedAccount)
                    {
                        // Prevent creating accounts that the user can never confirm.
                        var createdProfile = await _db.MemberProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
                        if (createdProfile is not null)
                        {
                            _db.MemberProfiles.Remove(createdProfile);
                            await _db.SaveChangesAsync();
                        }

                        await _userManager.DeleteAsync(user);
                        ModelState.AddModelError(string.Empty, "We couldn't send the confirmation email right now. Please try again later.");
                        return Page();
                    }
                }
            }

            if (_userManager.Options.SignIn.RequireConfirmedAccount)
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
