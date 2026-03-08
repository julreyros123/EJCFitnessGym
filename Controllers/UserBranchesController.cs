using System.Security.Claims;
using System.Text.RegularExpressions;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Admin;
using EJCFitnessGym.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Controllers
{
    [Authorize(Roles = "SuperAdmin")]
    [Route("Admin/UserBranches/[action]/{id?}")]
    public class UserBranchesController : Controller
    {
        private static readonly Regex BranchIdPattern = new(
            "^[A-Za-z0-9][A-Za-z0-9_-]{1,31}$",
            RegexOptions.Compiled);
        private const int MinLocationNameLength = 2;
        private const int MaxLocationNameLength = 120;

        private static readonly HashSet<string> ManagedRoles = new(StringComparer.Ordinal)
        {
            "Member",
            "Staff",
            "Finance",
            "Admin",
            "SuperAdmin"
        };

        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;

        public UserBranchesController(
            ApplicationDbContext db,
            UserManager<IdentityUser> userManager,
            SignInManager<IdentityUser> signInManager)
        {
            _db = db;
            _userManager = userManager;
            _signInManager = signInManager;
        }

        [HttpGet("/Admin/UserBranches")]
        public async Task<IActionResult> Index()
        {
            var currentUserId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(currentUserId))
            {
                return Challenge();
            }

            var isSuperAdmin = User.IsInRole("SuperAdmin");
            var users = await _userManager.Users
                .OrderBy(u => u.Email)
                .ToListAsync();

            var rolePairs = await (
                from userRole in _db.UserRoles
                join role in _db.Roles on userRole.RoleId equals role.Id
                select new
                {
                    userRole.UserId,
                    RoleName = role.Name ?? string.Empty
                })
                .ToListAsync();

            var rolesByUser = rolePairs
                .GroupBy(x => x.UserId, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.RoleName)
                        .Where(name => ManagedRoles.Contains(name))
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(name => name)
                        .ToList(),
                    StringComparer.Ordinal);

            var branchByUser = await _db.UserClaims
                .AsNoTracking()
                .Where(c => c.ClaimType == BranchAccess.BranchIdClaimType)
                .GroupBy(c => c.UserId)
                .Select(g => new
                {
                    UserId = g.Key,
                    BranchId = g.OrderByDescending(c => c.Id).Select(c => c.ClaimValue).FirstOrDefault()
                })
                .ToDictionaryAsync(x => x.UserId, x => x.BranchId, StringComparer.Ordinal);

            var assignmentCounts = branchByUser.Values
                .Where(branchId => !string.IsNullOrWhiteSpace(branchId))
                .Select(branchId => NormalizeBranchId(branchId!))
                .GroupBy(branchId => branchId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            var branchRecords = await _db.BranchRecords
                .AsNoTracking()
                .OrderByDescending(b => b.IsActive)
                .ThenBy(b => b.BranchId)
                .Select(b => new
                {
                    b.BranchId,
                    b.Name,
                    b.IsActive,
                    b.CreatedUtc
                })
                .ToListAsync();

            var branchNameById = branchRecords.ToDictionary(
                b => b.BranchId,
                b => BranchNaming.BuildDisplayName(b.Name),
                StringComparer.OrdinalIgnoreCase);

            var branchItems = branchRecords
                .Select(branch => new BranchDirectoryItemViewModel
                {
                    BranchId = branch.BranchId,
                    LocationName = BranchNaming.NormalizeLocationName(branch.Name),
                    DisplayName = BranchNaming.BuildDisplayName(branch.Name),
                    IsActive = branch.IsActive,
                    CreatedUtc = branch.CreatedUtc,
                    AssignedUserCount = assignmentCounts.TryGetValue(branch.BranchId, out var count) ? count : 0
                })
                .ToList();

            var items = users
                .Select(user =>
                {
                    rolesByUser.TryGetValue(user.Id, out var roles);
                    roles ??= new List<string>();

                    branchByUser.TryGetValue(user.Id, out var branchId);
                    var normalizedBranchId = string.IsNullOrWhiteSpace(branchId) ? null : NormalizeBranchId(branchId);
                    var email = user.Email ?? user.UserName ?? user.Id;
                    return new UserBranchAssignmentItemViewModel
                    {
                        UserId = user.Id,
                        Email = email,
                        RolesSummary = roles.Count == 0 ? "-" : string.Join(", ", roles),
                        BranchId = normalizedBranchId,
                        BranchDisplayName = normalizedBranchId is null
                            ? null
                            : branchNameById.TryGetValue(normalizedBranchId, out var branchName)
                                ? branchName
                                : null,
                        IsSuperAdmin = roles.Contains("SuperAdmin", StringComparer.Ordinal)
                    };
                })
                .Where(item => item.RolesSummary != "-")
                .ToList();

            if (!isSuperAdmin)
            {
                items = items
                    .Where(item => string.Equals(item.UserId, currentUserId, StringComparison.Ordinal))
                    .ToList();
            }

            var model = new UserBranchAssignmentListViewModel
            {
                IsSuperAdmin = isSuperAdmin,
                CurrentUserId = currentUserId,
                Branches = branchItems,
                Users = items
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateBranch(string locationName)
        {
            var normalizedLocationName = BranchNaming.NormalizeLocationName(locationName);
            if (normalizedLocationName.Length < MinLocationNameLength || normalizedLocationName.Length > MaxLocationNameLength)
            {
                TempData["StatusMessage"] = $"Location is required ({MinLocationNameLength}-{MaxLocationNameLength} characters).";
                return RedirectToAction(nameof(Index));
            }

            var normalizedBranchId = BranchNaming.GenerateBranchId(normalizedLocationName);
            if (!IsValidBranchId(normalizedBranchId))
            {
                TempData["StatusMessage"] = "Branch code could not be generated from the location. Use letters and numbers only.";
                return RedirectToAction(nameof(Index));
            }

            var existingBranches = await _db.BranchRecords
                .AsNoTracking()
                .Select(branch => new
                {
                    branch.BranchId,
                    branch.Name
                })
                .ToListAsync();

            var branchAlreadyExists = existingBranches.Any(branch =>
                string.Equals(branch.BranchId, normalizedBranchId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    BranchNaming.NormalizeLocationName(branch.Name),
                    normalizedLocationName,
                    StringComparison.OrdinalIgnoreCase));

            if (branchAlreadyExists)
            {
                TempData["StatusMessage"] = $"{BranchNaming.BuildDisplayName(normalizedLocationName)} already exists.";
                return RedirectToAction(nameof(Index));
            }

            var utcNow = DateTime.UtcNow;
            var branch = new BranchRecord
            {
                BranchId = normalizedBranchId,
                Name = normalizedLocationName,
                IsActive = true,
                CreatedUtc = utcNow,
                UpdatedUtc = utcNow,
                CreatedByUserId = _userManager.GetUserId(User)
            };

            _db.BranchRecords.Add(branch);
            await _db.SaveChangesAsync();

            TempData["StatusMessage"] = $"{BranchNaming.BuildDisplayName(normalizedLocationName)} created successfully ({normalizedBranchId}).";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleBranchStatus(string branchId)
        {
            var normalizedBranchId = NormalizeBranchId(branchId);
            if (!IsValidBranchId(normalizedBranchId))
            {
                TempData["StatusMessage"] = "Branch ID is invalid.";
                return RedirectToAction(nameof(Index));
            }

            var branch = await _db.BranchRecords
                .FirstOrDefaultAsync(b => b.BranchId == normalizedBranchId);

            if (branch is null)
            {
                TempData["StatusMessage"] = "Branch not found.";
                return RedirectToAction(nameof(Index));
            }

            branch.IsActive = !branch.IsActive;
            branch.UpdatedUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            var statusLabel = branch.IsActive ? "activated" : "deactivated";
            TempData["StatusMessage"] = $"{BranchNaming.BuildDisplayName(branch.Name)} is now {statusLabel}.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetBranch(string userId, string branchId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return BadRequest();
            }

            var normalizedBranchId = NormalizeBranchId(branchId);
            if (!IsValidBranchId(normalizedBranchId))
            {
                TempData["StatusMessage"] = "Branch ID is invalid. Use 2-32 chars: letters, numbers, dash, underscore.";
                return RedirectToAction(nameof(Index));
            }

            var branch = await _db.BranchRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.BranchId == normalizedBranchId);

            if (branch is null)
            {
                TempData["StatusMessage"] = $"Branch '{normalizedBranchId}' does not exist. Create it first before assigning users.";
                return RedirectToAction(nameof(Index));
            }

            if (!branch.IsActive)
            {
                TempData["StatusMessage"] = $"Branch '{normalizedBranchId}' is inactive. Activate it before assigning users.";
                return RedirectToAction(nameof(Index));
            }

            var targetUser = await _userManager.FindByIdAsync(userId);
            if (targetUser is null)
            {
                return NotFound();
            }

            if (!CanManageTargetUser(targetUser.Id))
            {
                return Forbid();
            }

            if (await _userManager.IsInRoleAsync(targetUser, "SuperAdmin"))
            {
                TempData["StatusMessage"] = "SuperAdmin accounts use global access and do not require branch assignment.";
                return RedirectToAction(nameof(Index));
            }

            var existingClaims = await _userManager.GetClaimsAsync(targetUser);
            var branchClaims = existingClaims
                .Where(c => c.Type == BranchAccess.BranchIdClaimType)
                .ToList();

            if (branchClaims.Count > 0)
            {
                var removeClaimsResult = await _userManager.RemoveClaimsAsync(targetUser, branchClaims);
                if (!removeClaimsResult.Succeeded)
                {
                    TempData["StatusMessage"] = string.Join("; ", removeClaimsResult.Errors.Select(e => e.Description));
                    return RedirectToAction(nameof(Index));
                }
            }

            var addClaimResult = await _userManager.AddClaimAsync(
                targetUser,
                new Claim(BranchAccess.BranchIdClaimType, normalizedBranchId));

            if (!addClaimResult.Succeeded)
            {
                TempData["StatusMessage"] = string.Join("; ", addClaimResult.Errors.Select(e => e.Description));
                return RedirectToAction(nameof(Index));
            }

            if (string.Equals(_userManager.GetUserId(User), targetUser.Id, StringComparison.Ordinal))
            {
                await _signInManager.RefreshSignInAsync(targetUser);
            }

            TempData["StatusMessage"] = $"Branch assignment saved for {targetUser.Email ?? targetUser.UserName} ({BranchNaming.BuildDisplayName(branch.Name)}).";
            return RedirectToAction(nameof(Index));
        }

        private bool CanManageTargetUser(string targetUserId)
        {
            var currentUserId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(currentUserId))
            {
                return false;
            }

            if (User.IsInRole("SuperAdmin"))
            {
                return true;
            }

            return string.Equals(currentUserId, targetUserId, StringComparison.Ordinal);
        }

        private static string NormalizeBranchId(string branchId) =>
            BranchNaming.NormalizeBranchId(branchId);

        private static bool IsValidBranchId(string branchId) => BranchIdPattern.IsMatch(branchId);
    }
}
