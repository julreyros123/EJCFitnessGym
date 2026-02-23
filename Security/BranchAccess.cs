using System.Security.Claims;

namespace EJCFitnessGym.Security
{
    public static class BranchAccess
    {
        public const string BranchIdClaimType = "branch_id";

        public static string? GetBranchId(this ClaimsPrincipal? user)
        {
            var branchId = user?.FindFirst(BranchIdClaimType)?.Value;
            return string.IsNullOrWhiteSpace(branchId) ? null : branchId.Trim();
        }

        public static bool HasBranchScope(this ClaimsPrincipal? user)
        {
            if (user?.Identity?.IsAuthenticated != true)
            {
                return false;
            }

            if (user.IsInRole("SuperAdmin"))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(user.GetBranchId());
        }
    }
}
