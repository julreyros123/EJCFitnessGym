using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace EJCFitnessGym.Services.Identity
{
    public class EmailVerificationCodeService : IEmailVerificationCodeService
    {
        private const string ProviderName = "EJC_EMAIL_VERIFICATION";
        private const string CodeHashTokenName = "CODE_HASH";
        private const string ExpiresUtcTokenName = "EXPIRES_UTC";
        private const string AttemptsTokenName = "ATTEMPTS";
        private const int ExpiryMinutes = 10;
        private const int MaxAttempts = 5;

        private readonly UserManager<IdentityUser> _userManager;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<EmailVerificationCodeService> _logger;

        public EmailVerificationCodeService(
            UserManager<IdentityUser> userManager,
            IEmailSender emailSender,
            ILogger<EmailVerificationCodeService> logger)
        {
            _userManager = userManager;
            _emailSender = emailSender;
            _logger = logger;
        }

        public async Task SendVerificationCodeAsync(IdentityUser user)
        {
            if (string.IsNullOrWhiteSpace(user.Email))
            {
                throw new InvalidOperationException("Cannot send verification code because user email is missing.");
            }

            var verificationCode = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
            var codeHash = await BuildCodeHashAsync(user, verificationCode);
            var expiresUtc = DateTime.UtcNow.AddMinutes(ExpiryMinutes);

            await _userManager.SetAuthenticationTokenAsync(user, ProviderName, CodeHashTokenName, codeHash);
            await _userManager.SetAuthenticationTokenAsync(user, ProviderName, ExpiresUtcTokenName, expiresUtc.ToString("O", CultureInfo.InvariantCulture));
            await _userManager.SetAuthenticationTokenAsync(user, ProviderName, AttemptsTokenName, "0");

            await _emailSender.SendEmailAsync(
                user.Email,
                "Your EJC verification code",
                $"Your 6-digit verification code is <strong>{verificationCode}</strong>. This code expires in {ExpiryMinutes} minutes.");
        }

        public async Task<EmailVerificationCodeResult> VerifyCodeAsync(IdentityUser user, string code)
        {
            var submittedCode = (code ?? string.Empty).Trim();
            if (!IsSixDigitCode(submittedCode))
            {
                return EmailVerificationCodeResult.Create(
                    EmailVerificationCodeStatus.Invalid,
                    "Enter a valid 6-digit verification code.");
            }

            var storedHash = await _userManager.GetAuthenticationTokenAsync(user, ProviderName, CodeHashTokenName);
            if (string.IsNullOrWhiteSpace(storedHash))
            {
                return EmailVerificationCodeResult.Create(
                    EmailVerificationCodeStatus.Missing,
                    "No active verification code found. Request a new code.");
            }

            var expiresRaw = await _userManager.GetAuthenticationTokenAsync(user, ProviderName, ExpiresUtcTokenName);
            if (!DateTime.TryParse(expiresRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiresUtc))
            {
                await ClearStoredCodeAsync(user);
                return EmailVerificationCodeResult.Create(
                    EmailVerificationCodeStatus.Missing,
                    "No active verification code found. Request a new code.");
            }

            if (expiresUtc <= DateTime.UtcNow)
            {
                await ClearStoredCodeAsync(user);
                return EmailVerificationCodeResult.Create(
                    EmailVerificationCodeStatus.Expired,
                    "Verification code expired. Request a new code.");
            }

            var attemptsRaw = await _userManager.GetAuthenticationTokenAsync(user, ProviderName, AttemptsTokenName);
            var attempts = int.TryParse(attemptsRaw, out var parsedAttempts) ? parsedAttempts : 0;
            if (attempts >= MaxAttempts)
            {
                await ClearStoredCodeAsync(user);
                return EmailVerificationCodeResult.Create(
                    EmailVerificationCodeStatus.TooManyAttempts,
                    "Too many attempts. Request a new verification code.");
            }

            var submittedHash = await BuildCodeHashAsync(user, submittedCode);
            if (!FixedTimeHashEquals(storedHash, submittedHash))
            {
                attempts++;
                await _userManager.SetAuthenticationTokenAsync(user, ProviderName, AttemptsTokenName, attempts.ToString(CultureInfo.InvariantCulture));
                if (attempts >= MaxAttempts)
                {
                    await ClearStoredCodeAsync(user);
                    return EmailVerificationCodeResult.Create(
                        EmailVerificationCodeStatus.TooManyAttempts,
                        "Too many attempts. Request a new verification code.");
                }

                return EmailVerificationCodeResult.Create(
                    EmailVerificationCodeStatus.Invalid,
                    "Invalid verification code.");
            }

            user.EmailConfirmed = true;
            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                var errors = string.Join("; ", updateResult.Errors.Select(e => e.Description));
                _logger.LogWarning("Email verification update failed for user {UserId}: {Errors}", user.Id, errors);
                return EmailVerificationCodeResult.Create(
                    EmailVerificationCodeStatus.Invalid,
                    "Could not verify your email right now. Please try again.");
            }

            await ClearStoredCodeAsync(user);
            return EmailVerificationCodeResult.Create(
                EmailVerificationCodeStatus.Success,
                "Your email has been verified. You can log in now.");
        }

        private async Task<string> BuildCodeHashAsync(IdentityUser user, string code)
        {
            var securityStamp = await _userManager.GetSecurityStampAsync(user) ?? string.Empty;
            var normalizedEmail = (user.Email ?? string.Empty).Trim().ToLowerInvariant();
            var payload = $"{user.Id}|{normalizedEmail}|{securityStamp}|{code}";
            var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            return Convert.ToHexString(hashBytes);
        }

        private async Task ClearStoredCodeAsync(IdentityUser user)
        {
            await _userManager.RemoveAuthenticationTokenAsync(user, ProviderName, CodeHashTokenName);
            await _userManager.RemoveAuthenticationTokenAsync(user, ProviderName, ExpiresUtcTokenName);
            await _userManager.RemoveAuthenticationTokenAsync(user, ProviderName, AttemptsTokenName);
        }

        private static bool IsSixDigitCode(string code)
        {
            if (code.Length != 6)
            {
                return false;
            }

            return code.All(char.IsDigit);
        }

        private static bool FixedTimeHashEquals(string leftHex, string rightHex)
        {
            if (string.IsNullOrWhiteSpace(leftHex) || string.IsNullOrWhiteSpace(rightHex))
            {
                return false;
            }

            try
            {
                var leftBytes = Convert.FromHexString(leftHex);
                var rightBytes = Convert.FromHexString(rightHex);
                return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
