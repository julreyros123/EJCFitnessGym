using Microsoft.AspNetCore.Http;

namespace EJCFitnessGym.Security
{
    public sealed class BranchScopeMiddleware
    {
        private readonly RequestDelegate _next;

        public BranchScopeMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!RequiresBackOfficeBranchScope(context.Request.Path))
            {
                await _next(context);
                return;
            }

            var user = context.User;
            if (user.Identity?.IsAuthenticated != true)
            {
                await _next(context);
                return;
            }

            var isBackOfficeUser =
                user.IsInRole("Staff") ||
                user.IsInRole("Admin") ||
                user.IsInRole("Finance") ||
                user.IsInRole("SuperAdmin");

            if (!isBackOfficeUser || user.HasBranchScope())
            {
                await _next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Branch assignment is required for back-office access.",
                    requiredClaim = BranchAccess.BranchIdClaimType
                });
                return;
            }

            await context.Response.WriteAsync("Branch assignment is required for back-office access.");
        }

        private static bool RequiresBackOfficeBranchScope(PathString path)
        {
            var value = path.Value ?? string.Empty;
            if (value.StartsWith("/Admin/UserBranches", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return value.StartsWith("/Admin", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("/Finance", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("/Staff", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("/Invoices", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("/SubscriptionPlans", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("/api/admin", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("/api/finance", StringComparison.OrdinalIgnoreCase);
        }
    }
}
