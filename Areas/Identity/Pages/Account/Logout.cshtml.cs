using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EJCFitnessGym.Areas.Identity.Pages.Account;

[AllowAnonymous]
[IgnoreAntiforgeryToken(Order = 1001)]
public class LogoutModel : PageModel
{
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly ILogger<LogoutModel> _logger;

    public LogoutModel(
        SignInManager<IdentityUser> signInManager,
        ILogger<LogoutModel> logger)
    {
        _signInManager = signInManager;
        _logger = logger;
    }

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null)
    {
        return await SignOutAndRedirectAsync(returnUrl);
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        return await SignOutAndRedirectAsync(returnUrl);
    }

    private async Task<IActionResult> SignOutAndRedirectAsync(string? returnUrl)
    {
        if (User?.Identity?.IsAuthenticated == true)
        {
            await _signInManager.SignOutAsync();
            _logger.LogInformation("User logged out.");
        }

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return LocalRedirect("~/");
    }
}
