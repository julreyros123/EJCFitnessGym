using Microsoft.AspNetCore.Identity;

namespace EJCFitnessGym.Services.Identity
{
    public interface IEmailVerificationCodeService
    {
        Task SendVerificationCodeAsync(IdentityUser user);
        Task<EmailVerificationCodeResult> VerifyCodeAsync(IdentityUser user, string code);
    }

    public enum EmailVerificationCodeStatus
    {
        Success,
        Missing,
        Expired,
        Invalid,
        TooManyAttempts
    }

    public sealed class EmailVerificationCodeResult
    {
        public EmailVerificationCodeStatus Status { get; init; }
        public string Message { get; init; } = string.Empty;
        public bool Succeeded => Status == EmailVerificationCodeStatus.Success;

        public static EmailVerificationCodeResult Create(EmailVerificationCodeStatus status, string message)
        {
            return new EmailVerificationCodeResult
            {
                Status = status,
                Message = message
            };
        }
    }
}
