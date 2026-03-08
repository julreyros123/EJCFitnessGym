using EJCFitnessGym.Data;
using EJCFitnessGym.Models;
using EJCFitnessGym.Models.Admin;
using EJCFitnessGym.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Services.Memberships
{
    public static class MemberBranchAssignment
    {
        public static async Task<string?> ResolveHomeBranchIdAsync(
            ApplicationDbContext db,
            string? memberUserId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(memberUserId))
            {
                return null;
            }

            var map = await ResolveHomeBranchMapAsync(db, [memberUserId.Trim()], cancellationToken);
            return map.TryGetValue(memberUserId.Trim(), out var branchId)
                ? NormalizeOptionalBranchId(branchId)
                : null;
        }

        public static async Task<Dictionary<string, string?>> ResolveHomeBranchMapAsync(
            ApplicationDbContext db,
            IEnumerable<string> memberUserIds,
            CancellationToken cancellationToken = default)
        {
            var normalizedIds = memberUserIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (normalizedIds.Count == 0)
            {
                return new Dictionary<string, string?>(StringComparer.Ordinal);
            }

            var branchByUserId = await db.MemberProfiles
                .AsNoTracking()
                .Where(profile => normalizedIds.Contains(profile.UserId))
                .Select(profile => new
                {
                    profile.UserId,
                    profile.HomeBranchId
                })
                .ToDictionaryAsync(
                    item => item.UserId,
                    item => NormalizeOptionalBranchId(item.HomeBranchId),
                    StringComparer.Ordinal,
                    cancellationToken);

            var missingIds = normalizedIds
                .Where(userId => !branchByUserId.TryGetValue(userId, out var branchId) || string.IsNullOrWhiteSpace(branchId))
                .ToList();

            if (missingIds.Count > 0)
            {
                var claimFallback = await db.UserClaims
                    .AsNoTracking()
                    .Where(claim =>
                        claim.ClaimType == BranchAccess.BranchIdClaimType &&
                        claim.ClaimValue != null &&
                        missingIds.Contains(claim.UserId))
                    .GroupBy(claim => claim.UserId)
                    .Select(group => new
                    {
                        UserId = group.Key,
                        BranchId = group
                            .OrderByDescending(claim => claim.Id)
                            .Select(claim => claim.ClaimValue)
                            .FirstOrDefault()
                    })
                    .ToListAsync(cancellationToken);

                foreach (var item in claimFallback)
                {
                    branchByUserId[item.UserId] = NormalizeOptionalBranchId(item.BranchId);
                }
            }

            foreach (var userId in normalizedIds)
            {
                branchByUserId.TryAdd(userId, null);
            }

            return branchByUserId;
        }

        public static async Task AssignHomeBranchAsync(
            ApplicationDbContext db,
            UserManager<IdentityUser> userManager,
            IdentityUser memberUser,
            string? branchId,
            MemberProfile? profile = null,
            CancellationToken cancellationToken = default)
        {
            var normalizedBranchId = NormalizeOptionalBranchId(branchId);

            profile ??= await db.MemberProfiles.FirstOrDefaultAsync(
                item => item.UserId == memberUser.Id,
                cancellationToken);

            if (profile is null)
            {
                profile = new MemberProfile
                {
                    UserId = memberUser.Id,
                    CreatedUtc = DateTime.UtcNow
                };
                db.MemberProfiles.Add(profile);
            }

            profile.HomeBranchId = normalizedBranchId;
            profile.UpdatedUtc = DateTime.UtcNow;

            var existingClaims = await userManager.GetClaimsAsync(memberUser);
            var branchClaims = existingClaims
                .Where(claim => claim.Type == BranchAccess.BranchIdClaimType)
                .ToList();

            if (branchClaims.Count > 0)
            {
                var removeResult = await userManager.RemoveClaimsAsync(memberUser, branchClaims);
                if (!removeResult.Succeeded)
                {
                    throw new InvalidOperationException(string.Join("; ", removeResult.Errors.Select(error => error.Description)));
                }
            }

            if (!string.IsNullOrWhiteSpace(normalizedBranchId))
            {
                var addResult = await userManager.AddClaimAsync(
                    memberUser,
                    new System.Security.Claims.Claim(BranchAccess.BranchIdClaimType, normalizedBranchId));

                if (!addResult.Succeeded)
                {
                    throw new InvalidOperationException(string.Join("; ", addResult.Errors.Select(error => error.Description)));
                }
            }
        }

        private static string? NormalizeOptionalBranchId(string? branchId)
        {
            var normalized = BranchNaming.NormalizeBranchId(branchId);
            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }
    }
}
