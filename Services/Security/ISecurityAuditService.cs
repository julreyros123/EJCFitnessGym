using System.Threading.Tasks;

namespace EJCFitnessGym.Services.Security
{
    public interface ISecurityAuditService
    {
        Task LogLoginSuccessAsync(string? userId, string email, string? ipAddress, string? userAgent);
        Task LogLoginFailureAsync(string email, string reason, string? ipAddress, string? userAgent);
        Task LogLogoutAsync(string? userId, string email, string? ipAddress, string? userAgent);
        Task LogAccountLockoutAsync(string? userId, string email, string? ipAddress, string? userAgent);
        Task LogUnauthorizedAccessAsync(string? userId, string email, string resource, string? ipAddress, string? userAgent);
    }
}
