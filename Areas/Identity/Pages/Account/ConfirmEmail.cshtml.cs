using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace EJCFitnessGym.Areas.Identity.Pages.Account;

public class ConfirmEmailModel : PageModel
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ILogger<ConfirmEmailModel> _logger;

    public ConfirmEmailModel(
        UserManager<IdentityUser> userManager,
        ILogger<ConfirmEmailModel> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public string ReturnUrl { get; private set; } = string.Empty;
    public string StatusMessage { get; private set; } = "Email confirmation failed.";
    public bool IsSuccess { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? userId = null, string? code = null, string? returnUrl = null)
    {
        ReturnUrl = AccountFlowHelper.NormalizeMemberReturnUrl(Url, returnUrl);

        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(code))
        {
            StatusMessage = "Invalid confirmation request.";
            return Page();
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            StatusMessage = $"Unable to load user with ID '{userId}'.";
            return Page();
        }

        try
        {
            var decodedCode = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
            var result = await _userManager.ConfirmEmailAsync(user, decodedCode);

            IsSuccess = result.Succeeded;
            StatusMessage = result.Succeeded
                ? "Thank you for confirming your email."
                : "Email confirmation failed. Request a new confirmation email and try again.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Email confirmation failed for user {UserId}.", userId);
            IsSuccess = false;
            StatusMessage = "Email confirmation failed. Request a new confirmation email and try again.";
        }

        return Page();
    }
}
