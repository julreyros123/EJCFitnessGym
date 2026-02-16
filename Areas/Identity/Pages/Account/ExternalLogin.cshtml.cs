using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class ExternalLoginModel : PageModel
{
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly IEmailSender _emailSender;
    private readonly IConfiguration _configuration;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<ExternalLoginModel> _logger;

    public ExternalLoginModel(
        SignInManager<IdentityUser> signInManager,
        UserManager<IdentityUser> userManager,
        IEmailSender emailSender,
        IConfiguration configuration,
        ApplicationDbContext db,
        ILogger<ExternalLoginModel> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _emailSender = emailSender;
        _configuration = configuration;
        _db = db;
        _logger = logger;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string ProviderDisplayName { get; set; } = string.Empty;
    public string ReturnUrl { get; set; } = string.Empty;

    [TempData]
    public string? ErrorMessage { get; set; }

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
    }

    public IActionResult OnGet()
    {
        return RedirectToPage("./Login");
    }

    public IActionResult OnPost(string provider, string? returnUrl = null)
    {
        var normalizedReturnUrl = AccountFlowHelper.NormalizeMemberReturnUrl(Url, returnUrl);
        var redirectUrl = Url.Page("./ExternalLogin", pageHandler: "Callback", values: new { returnUrl = normalizedReturnUrl });
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
        return Challenge(properties, provider);
    }

    public async Task<IActionResult> OnGetCallbackAsync(string? returnUrl = null, string? remoteError = null)
    {
        returnUrl = AccountFlowHelper.NormalizeMemberReturnUrl(Url, returnUrl);

        if (!string.IsNullOrWhiteSpace(remoteError))
        {
            ErrorMessage = $"External provider error: {remoteError}";
            return RedirectToPage("./Login", new { returnUrl });
        }

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            ErrorMessage = "Error loading external login information.";
            return RedirectToPage("./Login", new { returnUrl });
        }

        var loginResult = await _signInManager.ExternalLoginSignInAsync(
            info.LoginProvider,
            info.ProviderKey,
            isPersistent: false,
            bypassTwoFactor: true);

        if (loginResult.Succeeded)
        {
            var linkedUser = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (linkedUser is not null)
            {
                var roles = await _userManager.GetRolesAsync(linkedUser);
                if (roles.Any(AccountFlowHelper.IsBackOfficeRole))
                {
                    await _signInManager.SignOutAsync();
                    ErrorMessage = "Back-office accounts must use the dedicated staff/admin login.";
                    return RedirectToPage("./Login", new { returnUrl });
                }

                var memberRoleResult = await EnsureMemberRoleAsync(linkedUser);
                if (!memberRoleResult.Succeeded)
                {
                    await _signInManager.SignOutAsync();
                    ErrorMessage = "Unable to complete member sign-in. Please contact support.";
                    return RedirectToPage("./Login", new { returnUrl });
                }

                await EnsureMemberProfileAsync(linkedUser, info);
            }

            _logger.LogInformation("{LoginProvider} user logged in.", info.LoginProvider);
            return LocalRedirect(returnUrl);
        }

        if (loginResult.IsLockedOut)
        {
            return RedirectToPage("./Lockout");
        }

        ReturnUrl = returnUrl;
        ProviderDisplayName = info.ProviderDisplayName ?? info.LoginProvider;
        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email))
        {
            return Page();
        }

        var finalizeResult = await FinalizeExternalLoginAsync(info, email.Trim().ToLowerInvariant(), returnUrl);
        if (!finalizeResult.Succeeded)
        {
            foreach (var error in finalizeResult.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            Input.Email = email.Trim().ToLowerInvariant();
            return Page();
        }

        if (finalizeResult.RequiresEmailConfirmation)
        {
            return RedirectToPage("./RegisterConfirmation", new { email, returnUrl });
        }

        return LocalRedirect(returnUrl);
    }

    public async Task<IActionResult> OnPostConfirmationAsync(string? returnUrl = null)
    {
        returnUrl = AccountFlowHelper.NormalizeMemberReturnUrl(Url, returnUrl);
        ReturnUrl = returnUrl;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            ErrorMessage = "Error loading external login information during confirmation.";
            return RedirectToPage("./Login", new { returnUrl });
        }

        ProviderDisplayName = info.ProviderDisplayName ?? info.LoginProvider;
        var email = (Input.Email ?? string.Empty).Trim().ToLowerInvariant();
        var finalizeResult = await FinalizeExternalLoginAsync(info, email, returnUrl);
        if (!finalizeResult.Succeeded)
        {
            foreach (var error in finalizeResult.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            return Page();
        }

        if (finalizeResult.RequiresEmailConfirmation)
        {
            return RedirectToPage("./RegisterConfirmation", new { email, returnUrl });
        }

        return LocalRedirect(returnUrl);
    }

    private async Task<FinalizeExternalLoginResult> FinalizeExternalLoginAsync(
        ExternalLoginInfo info,
        string email,
        string returnUrl)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return FinalizeExternalLoginResult.Fail("A valid email address is required.");
        }

        var user = await _userManager.FindByEmailAsync(email);
        var isNewUser = false;

        if (user is null)
        {
            user = new IdentityUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = IsExternalEmailVerified(info)
            };

            var createResult = await _userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                return FinalizeExternalLoginResult.Fail(createResult.Errors.Select(e => e.Description));
            }

            isNewUser = true;
        }
        else
        {
            var existingRoles = await _userManager.GetRolesAsync(user);
            if (existingRoles.Any(AccountFlowHelper.IsBackOfficeRole))
            {
                await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
                return FinalizeExternalLoginResult.Fail(
                    "This account is assigned to a back-office role. Use the dedicated staff/admin login.");
            }
        }

        var addLoginResult = await _userManager.AddLoginAsync(user, info);
        if (!addLoginResult.Succeeded &&
            !addLoginResult.Errors.Any(e => string.Equals(e.Code, "LoginAlreadyAssociated", StringComparison.OrdinalIgnoreCase)))
        {
            return FinalizeExternalLoginResult.Fail(addLoginResult.Errors.Select(e => e.Description));
        }

        var roleResult = await EnsureMemberRoleAsync(user);
        if (!roleResult.Succeeded)
        {
            return FinalizeExternalLoginResult.Fail(roleResult.Errors.Select(e => e.Description));
        }

        await EnsureMemberProfileAsync(user, info);

        var requiresEmailConfirmation = _userManager.Options.SignIn.RequireConfirmedAccount && !user.EmailConfirmed;
        if (requiresEmailConfirmation)
        {
            await SendConfirmationEmailAsync(user, returnUrl);
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            return FinalizeExternalLoginResult.Success(requiresEmailConfirmation: true);
        }

        await _signInManager.SignInAsync(user, isPersistent: false, info.LoginProvider);

        if (isNewUser)
        {
            _logger.LogInformation(
                "Created a new member account through {Provider}. UserId={UserId}",
                info.LoginProvider,
                user.Id);
        }

        return FinalizeExternalLoginResult.Success();
    }

    private async Task<IdentityResult> EnsureMemberRoleAsync(IdentityUser user)
    {
        if (await _userManager.IsInRoleAsync(user, "Member"))
        {
            return IdentityResult.Success;
        }

        return await _userManager.AddToRoleAsync(user, "Member");
    }

    private async Task EnsureMemberProfileAsync(IdentityUser user, ExternalLoginInfo info)
    {
        var existingProfile = await _db.MemberProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
        var firstName = ResolveFirstName(info);
        var lastName = ResolveLastName(info);
        var phone = info.Principal.FindFirstValue(ClaimTypes.MobilePhone);
        var nowUtc = DateTime.UtcNow;

        if (existingProfile is null)
        {
            _db.MemberProfiles.Add(new MemberProfile
            {
                UserId = user.Id,
                FirstName = firstName,
                LastName = lastName,
                PhoneNumber = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
                CreatedUtc = nowUtc,
                UpdatedUtc = nowUtc
            });
            await _db.SaveChangesAsync();
            return;
        }

        var changed = false;
        if (string.IsNullOrWhiteSpace(existingProfile.FirstName) && !string.IsNullOrWhiteSpace(firstName))
        {
            existingProfile.FirstName = firstName;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(existingProfile.LastName) && !string.IsNullOrWhiteSpace(lastName))
        {
            existingProfile.LastName = lastName;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(existingProfile.PhoneNumber) && !string.IsNullOrWhiteSpace(phone))
        {
            existingProfile.PhoneNumber = phone.Trim();
            changed = true;
        }

        if (changed)
        {
            existingProfile.UpdatedUtc = nowUtc;
            await _db.SaveChangesAsync();
        }
    }

    private async Task SendConfirmationEmailAsync(IdentityUser user, string returnUrl)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            return;
        }

        var userId = await _userManager.GetUserIdAsync(user);
        var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));

        var callbackUrl = AccountFlowHelper.BuildAbsolutePageUrl(
            Url,
            Request,
            _configuration,
            "/Account/ConfirmEmail",
            new { area = "Identity", userId, code, returnUrl });

        if (string.IsNullOrWhiteSpace(callbackUrl))
        {
            return;
        }

        try
        {
            await _emailSender.SendEmailAsync(
                user.Email,
                "Confirm your email",
                $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not send confirmation email for external login user {UserId}.", user.Id);
        }
    }

    private static bool IsExternalEmailVerified(ExternalLoginInfo info)
    {
        var value = info.Principal.FindFirstValue("email_verified");
        if (bool.TryParse(value, out var isVerified))
        {
            return isVerified;
        }

        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveFirstName(ExternalLoginInfo info)
    {
        return info.Principal.FindFirstValue(ClaimTypes.GivenName) ??
               info.Principal.FindFirstValue("given_name") ??
               info.Principal.FindFirstValue(ClaimTypes.Name)?.Split(' ').FirstOrDefault();
    }

    private static string? ResolveLastName(ExternalLoginInfo info)
    {
        return info.Principal.FindFirstValue(ClaimTypes.Surname) ??
               info.Principal.FindFirstValue("family_name");
    }

    private sealed class FinalizeExternalLoginResult
    {
        public bool Succeeded { get; init; }
        public bool RequiresEmailConfirmation { get; init; }
        public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

        public static FinalizeExternalLoginResult Success(bool requiresEmailConfirmation = false)
        {
            return new FinalizeExternalLoginResult
            {
                Succeeded = true,
                RequiresEmailConfirmation = requiresEmailConfirmation
            };
        }

        public static FinalizeExternalLoginResult Fail(IEnumerable<string> errors)
        {
            return new FinalizeExternalLoginResult
            {
                Succeeded = false,
                Errors = errors.Where(e => !string.IsNullOrWhiteSpace(e)).ToArray()
            };
        }

        public static FinalizeExternalLoginResult Fail(string error)
        {
            return Fail(new[] { error });
        }
    }
}
