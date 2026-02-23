using System.ComponentModel.DataAnnotations;
using EJCFitnessGym.Services.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EJCFitnessGym.Areas.Identity.Pages.Account;

public class RegisterConfirmationModel : PageModel
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly IEmailVerificationCodeService _emailVerificationCodeService;

    public RegisterConfirmationModel(
        UserManager<IdentityUser> userManager,
        IEmailVerificationCodeService emailVerificationCodeService)
    {
        _userManager = userManager;
        _emailVerificationCodeService = emailVerificationCodeService;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required]
        [RegularExpression(@"^\d{6}$", ErrorMessage = "Enter a valid 6-digit verification code.")]
        [Display(Name = "Verification code")]
        public string Code { get; set; } = string.Empty;
    }

    public string Email { get; private set; } = string.Empty;
    public string ReturnUrl { get; private set; } = string.Empty;
    public bool IsVerified { get; private set; }

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
            StatusMessage = "Unable to load account details for email verification.";
            return Page();
        }

        if (user.EmailConfirmed)
        {
            IsVerified = true;
            StatusMessage = "Your email is already verified. You can sign in now.";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostVerifyAsync(string? email = null, string? returnUrl = null)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return RedirectToPage("./Register");
        }

        Email = email.Trim().ToLowerInvariant();
        ReturnUrl = AccountFlowHelper.NormalizeMemberReturnUrl(Url, returnUrl);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await _userManager.FindByEmailAsync(Email);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "Unable to load account details for email verification.");
            return Page();
        }

        if (user.EmailConfirmed)
        {
            IsVerified = true;
            StatusMessage = "Your email is already verified. You can sign in now.";
            return Page();
        }

        var verificationResult = await _emailVerificationCodeService.VerifyCodeAsync(user, Input.Code);
        if (!verificationResult.Succeeded)
        {
            ModelState.AddModelError(string.Empty, verificationResult.Message);
            return Page();
        }

        StatusMessage = verificationResult.Message;
        return RedirectToPage("./Login", new { returnUrl = ReturnUrl });
    }
}
