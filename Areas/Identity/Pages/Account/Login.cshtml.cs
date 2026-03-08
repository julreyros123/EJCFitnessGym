using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EJCFitnessGym.Areas.Identity.Pages.Account;

public class LoginModel(
    SignInManager<IdentityUser> signInManager,
    UserManager<IdentityUser> userManager,
    ILogger<LoginModel> logger,
    IWebHostEnvironment environment) : PageModel
{
    private readonly SignInManager<IdentityUser> _signInManager = signInManager;
    private readonly UserManager<IdentityUser> _userManager = userManager;
    private readonly ILogger<LoginModel> _logger = logger;
    private readonly IWebHostEnvironment _environment = environment;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IList<AuthenticationScheme> ExternalLogins { get; set; } = new List<AuthenticationScheme>();

    public string ReturnUrl { get; set; } = string.Empty;

    [TempData]
    public string? ErrorMessage { get; set; }

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null, string? googleError = null)
    {
        if (User?.Identity?.IsAuthenticated == true)
        {
            var signedInUser = await _userManager.GetUserAsync(User);
            if (signedInUser is not null)
            {
                var roles = await _userManager.GetRolesAsync(signedInUser);
                if (roles.Any(AccountFlowHelper.IsBackOfficeRole))
                {
                    var backOfficeReturnUrl = AccountFlowHelper.NormalizeBackOfficeReturnUrl(Url, returnUrl, roles);
                    return RedirectToPage("./BackOfficeLogin", new { returnUrl = backOfficeReturnUrl });
                }
            }

            var normalizedAuthenticatedReturnUrl = AccountFlowHelper.NormalizeMemberReturnUrl(Url, returnUrl);
            return LocalRedirect(normalizedAuthenticatedReturnUrl);
        }

        if (!string.IsNullOrEmpty(ErrorMessage))
        {
            ModelState.AddModelError(string.Empty, ErrorMessage);
        }
        else if (!string.IsNullOrWhiteSpace(googleError))
        {
            ModelState.AddModelError(string.Empty, googleError);
        }

        returnUrl = AccountFlowHelper.NormalizeMemberReturnUrl(Url, returnUrl);

        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

        ReturnUrl = returnUrl;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        var requestedReturnUrl = returnUrl;
        returnUrl = AccountFlowHelper.NormalizeMemberReturnUrl(Url, returnUrl);

        ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();
        Input.Email = (Input.Email ?? string.Empty).Trim().ToLowerInvariant();

        if (ModelState.IsValid)
        {
            var user = await _userManager.FindByEmailAsync(Input.Email);
            if (user is null)
            {
                ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                return Page();
            }

            if (!await _userManager.HasPasswordAsync(user))
            {
                // Accounts created via external providers (e.g., Google) may not have a local password.
                ModelState.AddModelError(string.Empty, "This account does not have a password. Please sign in using Google, or set a password in your account settings.");
                return Page();
            }

            var result = await _signInManager.PasswordSignInAsync(
                user,
                Input.Password,
                Input.RememberMe,
                lockoutOnFailure: true);

            if (result.Succeeded)
            {
                _logger.LogInformation("User logged in.");
                var roles = await _userManager.GetRolesAsync(user);

                // Backfill role for legacy accounts that were created before member-role auto assignment.
                if (roles.Count == 0)
                {
                    var addRoleResult = await _userManager.AddToRoleAsync(user, "Member");
                    if (addRoleResult.Succeeded)
                    {
                        await _signInManager.RefreshSignInAsync(user);
                        roles = await _userManager.GetRolesAsync(user);
                    }
                    else
                    {
                        var errors = string.Join(", ", addRoleResult.Errors.Select(e => e.Description));
                        _logger.LogWarning("Could not auto-assign Member role for {Email}: {Errors}", Input.Email, errors);
                    }
                }

                if (roles.Any(AccountFlowHelper.IsBackOfficeRole))
                {
                    await _signInManager.SignOutAsync();
                    ErrorMessage = "Back-office accounts must use the back-office login page.";
                    return RedirectToPage("./BackOfficeLogin", new
                    {
                        returnUrl = AccountFlowHelper.NormalizeBackOfficeReturnUrl(Url, requestedReturnUrl, roles)
                    });
                }

                var shouldUseRoleLandingPage =
                    string.IsNullOrWhiteSpace(returnUrl) ||
                    returnUrl.Equals("/", StringComparison.Ordinal) ||
                    returnUrl.Contains("/Identity/Account/AccessDenied", StringComparison.OrdinalIgnoreCase);

                var isMemberPortalUser = roles.Contains("Member") || roles.Count == 0;
                if (isMemberPortalUser &&
                    !string.IsNullOrWhiteSpace(returnUrl) &&
                    (returnUrl.StartsWith("/Admin", StringComparison.OrdinalIgnoreCase) ||
                     returnUrl.StartsWith("/Finance", StringComparison.OrdinalIgnoreCase) ||
                     returnUrl.StartsWith("/Staff", StringComparison.OrdinalIgnoreCase) ||
                     returnUrl.StartsWith("/Invoices", StringComparison.OrdinalIgnoreCase) ||
                     returnUrl.StartsWith("/SubscriptionPlans", StringComparison.OrdinalIgnoreCase)))
                {
                    shouldUseRoleLandingPage = true;
                }

                if (shouldUseRoleLandingPage)
                {
                    return RedirectToAction("Index", "Dashboard");
                }

                return LocalRedirect(returnUrl);
            }
            if (result.RequiresTwoFactor)
            {
                return RedirectToPage("./LoginWith2fa", new { ReturnUrl = returnUrl, RememberMe = Input.RememberMe });
            }
            if (result.IsLockedOut)
            {
                _logger.LogWarning("User account locked out.");
                return RedirectToPage("./Lockout", new { email = Input.Email });
            }

            if (result.IsNotAllowed)
            {
                if (user is not null)
                {
                    var isConfirmed = await _userManager.IsEmailConfirmedAsync(user);
                    if (!isConfirmed)
                    {
                        if (_environment.IsDevelopment())
                        {
                            ModelState.AddModelError(string.Empty, "Email verification is required in production, but is disabled in Development. Restart the app if you recently changed this setting.");
                        }
                        else
                        {
                            ModelState.AddModelError(string.Empty, "You must verify your email before you can log in.");
                        }
                        return Page();
                    }
                }
            }

            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
        }

        return Page();
    }
}
