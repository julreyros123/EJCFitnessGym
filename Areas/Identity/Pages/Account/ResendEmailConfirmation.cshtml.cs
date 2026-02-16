using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;

namespace EJCFitnessGym.Areas.Identity.Pages.Account;

public class ResendEmailConfirmationModel : PageModel
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly IEmailSender _emailSender;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ResendEmailConfirmationModel> _logger;

    public ResendEmailConfirmationModel(
        UserManager<IdentityUser> userManager,
        IEmailSender emailSender,
        IConfiguration configuration,
        ILogger<ResendEmailConfirmationModel> logger)
    {
        _userManager = userManager;
        _emailSender = emailSender;
        _configuration = configuration;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
    }

    public void OnGet(string? email = null)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            Input.Email = email.Trim().ToLowerInvariant();
        }
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var email = (Input.Email ?? string.Empty).Trim().ToLowerInvariant();
        Input.Email = email;
        var user = await _userManager.FindByEmailAsync(email);

        if (user is null || user.EmailConfirmed)
        {
            StatusMessage = "If an account exists and is not confirmed, a new confirmation email has been sent.";
            return RedirectToPage("./ResendEmailConfirmation");
        }

        var userId = await _userManager.GetUserIdAsync(user);
        var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));

        var normalizedReturnUrl = AccountFlowHelper.NormalizeMemberReturnUrl(Url, returnUrl);
        var callbackUrl = AccountFlowHelper.BuildAbsolutePageUrl(
            Url,
            Request,
            _configuration,
            "/Account/ConfirmEmail",
            new { area = "Identity", userId, code, returnUrl = normalizedReturnUrl });

        if (!string.IsNullOrWhiteSpace(callbackUrl))
        {
            try
            {
                await _emailSender.SendEmailAsync(
                    email,
                    "Confirm your email",
                    $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resend confirmation email to {Email}.", email);
                StatusMessage = "Could not send confirmation email right now. Please try again later.";
                return RedirectToPage("./ResendEmailConfirmation");
            }
        }

        StatusMessage = "Verification email sent. Please check your inbox.";
        return RedirectToPage("./ResendEmailConfirmation");
    }
}
