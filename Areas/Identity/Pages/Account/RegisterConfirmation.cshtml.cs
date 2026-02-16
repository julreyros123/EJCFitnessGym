using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EJCFitnessGym.Areas.Identity.Pages.Account;

public class RegisterConfirmationModel : PageModel
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public RegisterConfirmationModel(
        UserManager<IdentityUser> userManager,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _userManager = userManager;
        _configuration = configuration;
        _environment = environment;
    }

    public string Email { get; private set; } = string.Empty;
    public string ReturnUrl { get; private set; } = string.Empty;
    public string? ConfirmationLink { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(string? email = null, string? returnUrl = null)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return RedirectToPage("./Register");
        }

        Email = email.Trim().ToLowerInvariant();
        ReturnUrl = AccountFlowHelper.NormalizeMemberReturnUrl(Url, returnUrl);

        var user = await _userManager.FindByEmailAsync(Email);
        if (user is null)
        {
            StatusMessage = "Unable to load account details for email confirmation.";
            return Page();
        }

        if (user.EmailConfirmed)
        {
            StatusMessage = "Your email is already confirmed. You can sign in now.";
            return Page();
        }

        if (_environment.IsDevelopment())
        {
            var userId = await _userManager.GetUserIdAsync(user);
            var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var encodedCode = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(code));
            ConfirmationLink = AccountFlowHelper.BuildAbsolutePageUrl(
                Url,
                Request,
                _configuration,
                "/Account/ConfirmEmail",
                new { area = "Identity", userId, code = encodedCode, returnUrl = ReturnUrl });
        }

        return Page();
    }
}
