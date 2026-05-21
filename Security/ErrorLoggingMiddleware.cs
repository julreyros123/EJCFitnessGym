using System;
using System.Security.Claims;
using System.Threading.Tasks;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Admin;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EJCFitnessGym.Security
{
    public class ErrorLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ErrorLoggingMiddleware> _logger;

        public ErrorLoggingMiddleware(RequestDelegate next, ILogger<ErrorLoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unhandled exception occurred, logging to database...");
                await LogErrorToDatabase(context, ex);
                throw;
            }
        }

        private async Task LogErrorToDatabase(HttpContext context, Exception ex)
        {
            try
            {
                // We use a new scope to avoid issues if the current DbContext is already in a failed state
                using var scope = context.RequestServices.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var userId = context.User?.FindFirstValue(ClaimTypes.NameIdentifier);

                var errorLog = new SystemErrorLog
                {
                    ExceptionMessage = ex.Message,
                    StackTrace = ex.StackTrace,
                    Path = context.Request.Path,
                    UserId = userId,
                    TimestampUtc = DateTime.UtcNow
                };

                dbContext.SystemErrorLogs.Add(errorLog);
                await dbContext.SaveChangesAsync();
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, "Failed to log exception to database: {Message}", fallbackEx.Message);
            }
        }
    }
}
