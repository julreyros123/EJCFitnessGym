using EJCFitnessGym.Data;
using EJCFitnessGym.Areas.Identity.Pages.Account;
using EJCFitnessGym.Models.Admin;
using EJCFitnessGym.Models;
using EJCFitnessGym.Services.Identity;
using EJCFitnessGym.Services.Memberships;
using EJCFitnessGym.Security;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace EJCFitnessGym.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class GoogleAuthController : Controller
    {
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ApplicationDbContext _db;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GoogleAuthController> _logger;

        public GoogleAuthController(
            SignInManager<IdentityUser> signInManager,
            UserManager<IdentityUser> userManager,
            ApplicationDbContext db,
            IConfiguration configuration,
            ILogger<GoogleAuthController> logger)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _db = db;
            _configuration = configuration;
            _logger = logger;
        }

        [HttpPost("signin")]
        public async Task<IActionResult> SignIn(
            [FromForm] string credential,
            [FromForm(Name = "g_csrf_token")] string? csrfToken,
            [FromQuery] string? returnUrl = null,
            [FromQuery] string? origin = null,
            CancellationToken cancellationToken = default)
        {
            var normalizedReturnUrl = AccountFlowHelper.NormalizeMemberReturnUrl(Url, returnUrl);
            var authPage = ResolveAuthPage(origin);

            if (string.IsNullOrWhiteSpace(credential))
            {
                return RedirectToAuthPage(authPage, normalizedReturnUrl, "Google did not return a sign-in credential.");
            }

            if (!HasValidGoogleCsrfToken(csrfToken))
            {
                return RedirectToAuthPage(authPage, normalizedReturnUrl, "Google sign-in could not be verified. Please try again.");
            }

            var clientId = _configuration["Authentication:Google:ClientId"]?.Trim();
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return RedirectToAuthPage(authPage, normalizedReturnUrl, "Google sign-in is not configured for this app.");
            }

            try
            {
                var payload = await GoogleJsonWebSignature.ValidateAsync(
                    credential,
                    new GoogleJsonWebSignature.ValidationSettings
                    {
                        Audience = [clientId]
                    });

                if (!payload.EmailVerified || string.IsNullOrWhiteSpace(payload.Email))
                {
                    return RedirectToAuthPage(authPage, normalizedReturnUrl, "Google did not provide a verified email address.");
                }

                var email = payload.Email.Trim().ToLowerInvariant();
                var user = await _userManager.FindByEmailAsync(email);
                if (user == null)
                {
                    user = new IdentityUser
                    {
                        UserName = email,
                        Email = email,
                        EmailConfirmed = true
                    };

                    var result = await _userManager.CreateAsync(user);
                    if (!result.Succeeded)
                    {
                        var errors = string.Join(", ", result.Errors.Select(error => error.Description));
                        _logger.LogWarning("Google sign-in could not create member account for {Email}: {Errors}", email, errors);
                        return RedirectToAuthPage(authPage, normalizedReturnUrl, "We could not create your member account right now.");
                    }
                }

                var roles = await _userManager.GetRolesAsync(user);
                if (roles.Any(AccountFlowHelper.IsBackOfficeRole))
                {
                    await _signInManager.SignOutAsync();
                    return RedirectToAuthPage(authPage, normalizedReturnUrl, "Back-office accounts must use the dedicated login.");
                }

                if (!roles.Contains("Member", StringComparer.OrdinalIgnoreCase))
                {
                    var addMemberRoleResult = await _userManager.AddToRoleAsync(user, "Member");
                    if (!addMemberRoleResult.Succeeded)
                    {
                        var errors = string.Join(", ", addMemberRoleResult.Errors.Select(error => error.Description));
                        _logger.LogWarning("Google sign-in could not assign Member role for {Email}: {Errors}", email, errors);
                        return RedirectToAuthPage(authPage, normalizedReturnUrl, "We could not finish Google sign-in right now.");
                    }
                }

                await EnsureMemberProfileAsync(user, payload, cancellationToken);

                await _signInManager.SignInAsync(user, isPersistent: false);

                if (string.IsNullOrWhiteSpace(normalizedReturnUrl) ||
                    normalizedReturnUrl.Equals("/", StringComparison.Ordinal) ||
                    normalizedReturnUrl.Contains("/Identity/Account/AccessDenied", StringComparison.OrdinalIgnoreCase))
                {
                    return RedirectToAction("Index", "Dashboard");
                }

                return LocalRedirect(normalizedReturnUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Google GSI Sign-in failed.");
                return RedirectToAuthPage(authPage, normalizedReturnUrl, "Google sign-in failed. Please try again.");
            }
        }

        private async Task EnsureMemberProfileAsync(
            IdentityUser user,
            GoogleJsonWebSignature.Payload payload,
            CancellationToken cancellationToken)
        {
            var profile = await _db.MemberProfiles.FirstOrDefaultAsync(
                memberProfile => memberProfile.UserId == user.Id,
                cancellationToken);
            var nowUtc = DateTime.UtcNow;
            var shouldSave = false;

            if (profile is null)
            {
                profile = new MemberProfile
                {
                    UserId = user.Id,
                    FirstName = payload.GivenName ?? string.Empty,
                    LastName = payload.FamilyName ?? string.Empty,
                    CreatedUtc = nowUtc,
                    UpdatedUtc = nowUtc
                };
                _db.MemberProfiles.Add(profile);
                shouldSave = true;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(profile.FirstName) && !string.IsNullOrWhiteSpace(payload.GivenName))
                {
                    profile.FirstName = payload.GivenName;
                    shouldSave = true;
                }

                if (string.IsNullOrWhiteSpace(profile.LastName) && !string.IsNullOrWhiteSpace(payload.FamilyName))
                {
                    profile.LastName = payload.FamilyName;
                    shouldSave = true;
                }
            }

            if (string.IsNullOrWhiteSpace(profile.HomeBranchId))
            {
                var registrationBranchId = await ResolveRegistrationBranchIdAsync(cancellationToken);
                await MemberBranchAssignment.AssignHomeBranchAsync(
                    _db,
                    _userManager,
                    user,
                    registrationBranchId,
                    profile,
                    cancellationToken);
                shouldSave = true;
            }

            if (shouldSave)
            {
                profile.UpdatedUtc = nowUtc;
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        private async Task<string?> ResolveRegistrationBranchIdAsync(CancellationToken cancellationToken)
        {
            var configuredBranchId = _configuration["BranchAccess:DefaultBranchId"]?.Trim();
            if (!string.IsNullOrWhiteSpace(configuredBranchId))
            {
                return configuredBranchId;
            }

            var activeBranchId = await _db.BranchRecords
                .AsNoTracking()
                .Where(branch => branch.IsActive)
                .OrderBy(branch => branch.BranchId)
                .Select(branch => branch.BranchId)
                .FirstOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(activeBranchId))
            {
                return activeBranchId;
            }

            var fallbackBranchId = await _db.BranchRecords
                .AsNoTracking()
                .OrderBy(branch => branch.BranchId)
                .Select(branch => branch.BranchId)
                .FirstOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(fallbackBranchId))
            {
                return fallbackBranchId.Trim();
            }

            var existingClaimBranchId = await _db.UserClaims
                .AsNoTracking()
                .Where(claim =>
                    claim.ClaimType == BranchAccess.BranchIdClaimType &&
                    claim.ClaimValue != null)
                .OrderByDescending(claim => claim.Id)
                .Select(claim => claim.ClaimValue)
                .FirstOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(existingClaimBranchId))
            {
                return existingClaimBranchId.Trim();
            }

            const string bootstrapBranchId = BranchNaming.DefaultBranchId;
            const string bootstrapBranchName = BranchNaming.DefaultLocationName;

            try
            {
                _db.BranchRecords.Add(new BranchRecord
                {
                    BranchId = bootstrapBranchId,
                    Name = bootstrapBranchName,
                    IsActive = true,
                    CreatedUtc = DateTime.UtcNow,
                    UpdatedUtc = DateTime.UtcNow
                });
                await _db.SaveChangesAsync(cancellationToken);
                return bootstrapBranchId;
            }
            catch (DbUpdateException)
            {
                var seededBranchId = await _db.BranchRecords
                    .AsNoTracking()
                    .Where(branch => branch.BranchId == bootstrapBranchId)
                    .Select(branch => branch.BranchId)
                    .FirstOrDefaultAsync(cancellationToken);

                return string.IsNullOrWhiteSpace(seededBranchId) ? null : seededBranchId.Trim();
            }
        }

        private bool HasValidGoogleCsrfToken(string? requestToken)
        {
            if (!Request.Cookies.TryGetValue("g_csrf_token", out var cookieToken) ||
                string.IsNullOrWhiteSpace(cookieToken) ||
                string.IsNullOrWhiteSpace(requestToken))
            {
                return false;
            }

            var cookieBytes = Encoding.UTF8.GetBytes(cookieToken);
            var requestBytes = Encoding.UTF8.GetBytes(requestToken);
            return CryptographicOperations.FixedTimeEquals(cookieBytes, requestBytes);
        }

        private IActionResult RedirectToAuthPage(string authPage, string returnUrl, string errorMessage)
        {
            var destination = Url.Page(authPage, values: new
            {
                area = "Identity",
                returnUrl,
                googleError = errorMessage
            }) ?? "/Identity/Account/Login";

            return Redirect(destination);
        }

        private static string ResolveAuthPage(string? origin)
        {
            return string.Equals(origin, "register", StringComparison.OrdinalIgnoreCase)
                ? "/Account/Register"
                : "/Account/Login";
        }
    }
}
