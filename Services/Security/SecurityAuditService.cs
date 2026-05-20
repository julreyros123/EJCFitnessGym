using System;
using System.Threading.Tasks;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Admin;

namespace EJCFitnessGym.Services.Security
{
    public class SecurityAuditService : ISecurityAuditService
    {
        private readonly ApplicationDbContext _context;

        public SecurityAuditService(ApplicationDbContext context)
        {
            _context = context;
        }

        private async Task LogEventAsync(string eventType, string status, string? userId, string? email, string? details, string? ipAddress, string? userAgent)
        {
            var log = new SecurityAuditLog
            {
                EventType = eventType,
                EventStatus = status,
                UserId = userId,
                Email = email,
                EventDetails = details,
                IpAddress = ipAddress,
                UserAgent = userAgent?.Length > 500 ? userAgent.Substring(0, 500) : userAgent,
                EventTimestampUtc = DateTime.UtcNow
            };

            _context.SecurityAuditLogs.Add(log);
            await _context.SaveChangesAsync();
        }

        public Task LogLoginSuccessAsync(string? userId, string email, string? ipAddress, string? userAgent)
        {
            return LogEventAsync("LoginSuccess", "Success", userId, email, "Successfully logged in", ipAddress, userAgent);
        }

        public Task LogLoginFailureAsync(string email, string reason, string? ipAddress, string? userAgent)
        {
            return LogEventAsync("LoginFailure", "Failure", null, email, $"Failed login attempt: {reason}", ipAddress, userAgent);
        }

        public Task LogLogoutAsync(string? userId, string email, string? ipAddress, string? userAgent)
        {
            return LogEventAsync("Logout", "Success", userId, email, "User logged out", ipAddress, userAgent);
        }

        public Task LogAccountLockoutAsync(string? userId, string email, string? ipAddress, string? userAgent)
        {
            return LogEventAsync("AccountLockout", "Critical", userId, email, "Account locked out due to multiple failed login attempts", ipAddress, userAgent);
        }

        public Task LogUnauthorizedAccessAsync(string? userId, string email, string resource, string? ipAddress, string? userAgent)
        {
            return LogEventAsync("UnauthorizedAccess", "Warning", userId, email, $"Unauthorized access attempt to {resource}", ipAddress, userAgent);
        }
    }
}
