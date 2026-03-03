using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EJCFitnessGym.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class BackOfficeLoginModel : PageModel
{
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ILogger<BackOfficeLoginModel> _logger;

    public BackOfficeLoginModel(
        SignInManager<IdentityUser> signInManager,
        UserManager<IdentityUser> userManager,
        ILogger<BackOfficeLoginModel> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string ReturnUrl { get; set; } = string.Empty;

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

    public async Task OnGetAsync(string? returnUrl = null)
    {
        ReturnUrl = AccountFlowHelper.NormalizeBackOfficeReturnUrl(Url, returnUrl, roles: Array.Empty<string>());
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = AccountFlowHelper.NormalizeBackOfficeReturnUrl(Url, returnUrl, roles: Array.Empty<string>());
        Input.Email = (Input.Email ?? string.Empty).Trim().ToLowerInvariant();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await _userManager.FindByEmailAsync(Input.Email);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
            return Page();
        }

        if (!await _userManager.HasPasswordAsync(user))
        {
            ModelState.AddModelError(string.Empty, "This account does not have a password. Please sign in using the member portal.");
            return Page();
        }

        var result = await _signInManager.PasswordSignInAsync(user, Input.Password, Input.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            var roles = await _userManager.GetRolesAsync(user);
            var backOfficeRoles = roles.Where(AccountFlowHelper.IsBackOfficeRole).ToArray();
            if (backOfficeRoles.Length == 0)
            {
                await _signInManager.SignOutAsync();
                ModelState.AddModelError(string.Empty, "This login is for back-office roles only.");
                return Page();
            }

            var target = AccountFlowHelper.NormalizeBackOfficeReturnUrl(Url, returnUrl, roles);
            _logger.LogInformation(
                "Back-office login success for {Email}. Roles={Roles}",
                Input.Email,
                string.Join(", ", backOfficeRoles));

            return LocalRedirect(target);
        }

        if (result.RequiresTwoFactor)
        {
            return RedirectToPage("./LoginWith2fa", new { ReturnUrl = ReturnUrl, RememberMe = Input.RememberMe });
        }

        if (result.IsNotAllowed)
        {
            ModelState.AddModelError(string.Empty, "You must verify your email before you can log in.");
            return Page();
        }

        if (result.IsLockedOut)
        {
            return RedirectToPage("./Lockout", new { email = Input.Email });
        }

        ModelState.AddModelError(string.Empty, "Invalid login attempt.");
        return Page();
    }
}
