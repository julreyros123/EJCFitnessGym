using System.ComponentModel.DataAnnotations;
using EJCFitnessGym.Services.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EJCFitnessGym.Areas.Identity.Pages.Account;

public class ResendEmailConfirmationModel : PageModel
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly IEmailVerificationCodeService _emailVerificationCodeService;
    private readonly ILogger<ResendEmailConfirmationModel> _logger;

    public ResendEmailConfirmationModel(
        UserManager<IdentityUser> userManager,
        IEmailVerificationCodeService emailVerificationCodeService,
        ILogger<ResendEmailConfirmationModel> logger)
    {
        _userManager = userManager;
        _emailVerificationCodeService = emailVerificationCodeService;
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

    public async Task<IActionResult> OnPostAsync()
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
            StatusMessage = "If an account exists and is not verified, a new verification code has been sent.";
            return RedirectToPage("./ResendEmailConfirmation", new { email });
        }

        try
        {
            await _emailVerificationCodeService.SendVerificationCodeAsync(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resend verification code to {Email}.", email);
            StatusMessage = "Could not send verification code right now. Please try again later.";
            return RedirectToPage("./ResendEmailConfirmation", new { email });
        }

        StatusMessage = "Verification code sent. Please check your inbox.";
        return RedirectToPage("./ResendEmailConfirmation", new { email });
    }
}
