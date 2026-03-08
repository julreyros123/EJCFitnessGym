using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Admin;
using EJCFitnessGym.Security;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Pages.Admin
{
    public class SettingsModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IConfiguration _configuration;

        public SettingsModel(ApplicationDbContext db, IConfiguration configuration)
        {
            _db = db;
            _configuration = configuration;
        }

        public string ScopeLabel { get; private set; } = "All Branches";
        public string BranchSummary { get; private set; } = "Not scoped";
        public bool RequireConfirmedEmail { get; private set; }
        public bool UseSecureCookies { get; private set; }
        public int AdminUserCount { get; private set; }
        public int StaffUserCount { get; private set; }
        public int FinanceUserCount { get; private set; }
        public int MemberUserCount { get; private set; }

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            var isSuperAdmin = User.IsInRole("SuperAdmin");
            var branchId = isSuperAdmin ? null : User.GetBranchId();

            ScopeLabel = string.IsNullOrWhiteSpace(branchId)
                ? "All Branches"
                : $"Branch {branchId}";

            if (string.IsNullOrWhiteSpace(branchId))
            {
                BranchSummary = "Global scope";
            }
            else
            {
                var branch = await _db.BranchRecords
                    .AsNoTracking()
                    .FirstOrDefaultAsync(b => b.BranchId == branchId, cancellationToken);
                BranchSummary = branch is null
                    ? $"Scoped to {branchId}"
                    : $"{BranchNaming.BuildDisplayName(branch.Name)} ({branch.BranchId})";
            }

            RequireConfirmedEmail = _configuration.GetValue<bool?>("Identity:RequireConfirmedEmail") ?? false;
            UseSecureCookies = _configuration.GetValue<bool?>("Security:UseSecureCookies") ?? true;

            var trackedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "SuperAdmin",
                "Admin",
                "Staff",
                "Finance",
                "Member"
            };

            var roleAssignmentsQuery =
                from userRole in _db.UserRoles.AsNoTracking()
                join role in _db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                where trackedRoles.Contains(role.Name ?? string.Empty)
                select new
                {
                    userRole.UserId,
                    RoleName = role.Name ?? string.Empty
                };

            if (!string.IsNullOrWhiteSpace(branchId))
            {
                var scopedUserIds = await _db.UserClaims
                    .AsNoTracking()
                    .Where(claim =>
                        claim.ClaimType == BranchAccess.BranchIdClaimType &&
                        claim.ClaimValue == branchId)
                    .Select(claim => claim.UserId)
                    .Distinct()
                    .ToListAsync(cancellationToken);

                roleAssignmentsQuery = roleAssignmentsQuery
                    .Where(assignment => scopedUserIds.Contains(assignment.UserId));
            }

            var roleAssignments = await roleAssignmentsQuery.ToListAsync(cancellationToken);
            var roleCounts = roleAssignments
                .GroupBy(a => a.RoleName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(entry => entry.UserId).Distinct(StringComparer.Ordinal).Count(),
                    StringComparer.OrdinalIgnoreCase);

            var superAdmins = GetRoleCount(roleCounts, "SuperAdmin");
            var admins = GetRoleCount(roleCounts, "Admin");
            AdminUserCount = superAdmins + admins;
            StaffUserCount = GetRoleCount(roleCounts, "Staff");
            FinanceUserCount = GetRoleCount(roleCounts, "Finance");
            MemberUserCount = GetRoleCount(roleCounts, "Member");
        }

        private static int GetRoleCount(
            IReadOnlyDictionary<string, int> roleCounts,
            string roleName)
        {
            return roleCounts.TryGetValue(roleName, out var count) ? count : 0;
        }
    }
}
