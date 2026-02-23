using System.Globalization;
using System.Security.Claims;
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
    [Route("Admin/BackOfficeAccounts/[action]/{id?}")]
    public class BackOfficeAccountsController : Controller
    {
        private const string BackOfficeCreatedByClaimType = "backoffice_created_by_user_id";
        private const string BackOfficeCreatedUtcClaimType = "backoffice_created_utc";
        private const string BackOfficeStatusClaimType = "backoffice_account_status";
        private const string BackOfficeStatusChangedByClaimType = "backoffice_status_changed_by_user_id";
        private const string BackOfficeStatusChangedUtcClaimType = "backoffice_status_changed_utc";
        private const string BackOfficeStatusActiveValue = "active";
        private const string BackOfficeStatusInactiveValue = "inactive";
        private static readonly string[] ManagedRoles = { "Admin", "Finance" };

        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;

        public BackOfficeAccountsController(ApplicationDbContext db, UserManager<IdentityUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        [HttpGet("/Admin/BackOfficeAccounts")]
        public async Task<IActionResult> Index()
        {
            var model = await BuildIndexModelAsync();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(BackOfficeAccountCreateInputViewModel input)
        {
            input.Email = (input.Email ?? string.Empty).Trim();
            input.PhoneNumber = string.IsNullOrWhiteSpace(input.PhoneNumber) ? null : input.PhoneNumber.Trim();
            input.Role = NormalizeRole(input.Role) ?? string.Empty;
            input.BranchId = NormalizeBranchId(input.BranchId) ?? string.Empty;

            if (string.IsNullOrWhiteSpace(input.Role))
            {
                ModelState.AddModelError(nameof(input.Role), "Role is required.");
            }
            else if (!IsManagedRole(input.Role))
            {
                ModelState.AddModelError(nameof(input.Role), "Selected role is invalid.");
            }

            if (string.IsNullOrWhiteSpace(input.BranchId))
            {
                ModelState.AddModelError(nameof(input.BranchId), "Branch is required.");
            }
            else
            {
                var hasActiveBranch = await _db.BranchRecords
                    .AsNoTracking()
                    .AnyAsync(branch => branch.IsActive && branch.BranchId == input.BranchId);
                if (!hasActiveBranch)
                {
                    ModelState.AddModelError(nameof(input.BranchId), "Selected branch is invalid or inactive.");
                }
            }

            if (!ModelState.IsValid)
            {
                var invalidModel = await BuildIndexModelAsync(input);
                return View(nameof(Index), invalidModel);
            }

            var existingUser = await _userManager.FindByEmailAsync(input.Email);
            if (existingUser is not null)
            {
                ModelState.AddModelError(nameof(input.Email), "A user with this email already exists.");
                var duplicateModel = await BuildIndexModelAsync(input);
                return View(nameof(Index), duplicateModel);
            }

            var user = new IdentityUser
            {
                UserName = input.Email,
                Email = input.Email,
                EmailConfirmed = true,
                PhoneNumber = input.PhoneNumber
            };

            var createResult = await _userManager.CreateAsync(user, input.Password);
            if (!createResult.Succeeded)
            {
                AddIdentityErrors(createResult);
                var createFailedModel = await BuildIndexModelAsync(input);
                return View(nameof(Index), createFailedModel);
            }

            var roleResult = await _userManager.AddToRoleAsync(user, input.Role);
            if (!roleResult.Succeeded)
            {
                await _userManager.DeleteAsync(user);
                AddIdentityErrors(roleResult);
                var roleFailedModel = await BuildIndexModelAsync(input);
                return View(nameof(Index), roleFailedModel);
            }

            var branchClaimResult = await _userManager.AddClaimAsync(
                user,
                new Claim(BranchAccess.BranchIdClaimType, input.BranchId));
            if (!branchClaimResult.Succeeded)
            {
                await _userManager.RemoveFromRoleAsync(user, input.Role);
                await _userManager.DeleteAsync(user);
                AddIdentityErrors(branchClaimResult);
                var branchClaimFailedModel = await BuildIndexModelAsync(input);
                return View(nameof(Index), branchClaimFailedModel);
            }

            var auditClaims = new List<Claim>();
            var createdByUserId = _userManager.GetUserId(User);
            if (!string.IsNullOrWhiteSpace(createdByUserId))
            {
                auditClaims.Add(new Claim(BackOfficeCreatedByClaimType, createdByUserId));
                auditClaims.Add(new Claim(BackOfficeStatusChangedByClaimType, createdByUserId));
            }

            var createdUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            auditClaims.Add(new Claim(
                BackOfficeCreatedUtcClaimType,
                createdUtc));
            auditClaims.Add(new Claim(BackOfficeStatusClaimType, BackOfficeStatusActiveValue));
            auditClaims.Add(new Claim(BackOfficeStatusChangedUtcClaimType, createdUtc));

            var auditClaimResult = await _userManager.AddClaimsAsync(user, auditClaims);
            if (!auditClaimResult.Succeeded)
            {
                await _userManager.RemoveFromRoleAsync(user, input.Role);
                await _userManager.DeleteAsync(user);
                AddIdentityErrors(auditClaimResult);
                var auditClaimFailedModel = await BuildIndexModelAsync(input);
                return View(nameof(Index), auditClaimFailedModel);
            }

            TempData["StatusMessage"] = "Back office account created successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                TempData["StatusMessage"] = "Account was not specified.";
                return RedirectToAction(nameof(Index));
            }

            var actorUserId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return Challenge();
            }

            var targetUser = await _userManager.FindByIdAsync(userId);
            if (targetUser is null)
            {
                TempData["StatusMessage"] = "Account was not found.";
                return RedirectToAction(nameof(Index));
            }

            if (string.Equals(targetUser.Id, actorUserId, StringComparison.Ordinal))
            {
                TempData["StatusMessage"] = "You cannot deactivate your own account.";
                return RedirectToAction(nameof(Index));
            }

            var targetRoles = await _userManager.GetRolesAsync(targetUser);
            var isManagedAccount = targetRoles.Any(IsManagedRole);
            if (!isManagedAccount || targetRoles.Contains("SuperAdmin", StringComparer.OrdinalIgnoreCase))
            {
                TempData["StatusMessage"] = "Only Admin and Finance accounts can be managed here.";
                return RedirectToAction(nameof(Index));
            }

            var currentStatusClaim = await _db.UserClaims
                .AsNoTracking()
                .Where(claim =>
                    claim.UserId == targetUser.Id &&
                    claim.ClaimType == BackOfficeStatusClaimType)
                .OrderByDescending(claim => claim.Id)
                .Select(claim => claim.ClaimValue)
                .FirstOrDefaultAsync();

            var isActive = ResolveIsActive(currentStatusClaim, targetUser);
            if (!targetUser.LockoutEnabled)
            {
                var enableLockoutResult = await _userManager.SetLockoutEnabledAsync(targetUser, true);
                if (!enableLockoutResult.Succeeded)
                {
                    AddIdentityErrors(enableLockoutResult);
                    TempData["StatusMessage"] = "Could not update account status. Please try again.";
                    return RedirectToAction(nameof(Index));
                }
            }

            var nextStatusValue = isActive ? BackOfficeStatusInactiveValue : BackOfficeStatusActiveValue;
            var nextLockoutEnd = isActive ? DateTimeOffset.MaxValue : (DateTimeOffset?)null;
            var updateResult = await _userManager.SetLockoutEndDateAsync(targetUser, nextLockoutEnd);
            if (!updateResult.Succeeded)
            {
                AddIdentityErrors(updateResult);
                TempData["StatusMessage"] = "Could not update account status. Please try again.";
                return RedirectToAction(nameof(Index));
            }

            if (!isActive)
            {
                _ = await _userManager.ResetAccessFailedCountAsync(targetUser);
            }

            var statusAuditClaims = new List<Claim>
            {
                new(BackOfficeStatusClaimType, nextStatusValue),
                new(
                    BackOfficeStatusChangedUtcClaimType,
                    DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
                new(BackOfficeStatusChangedByClaimType, actorUserId)
            };

            var statusAuditResult = await _userManager.AddClaimsAsync(targetUser, statusAuditClaims);
            if (!statusAuditResult.Succeeded)
            {
                AddIdentityErrors(statusAuditResult);
                TempData["StatusMessage"] = "Account status changed, but audit record failed to save.";
                return RedirectToAction(nameof(Index));
            }

            var statusText = isActive ? "deactivated" : "activated";
            TempData["StatusMessage"] = $"Account '{targetUser.Email ?? targetUser.UserName ?? targetUser.Id}' is now {statusText}.";
            return RedirectToAction(nameof(Index));
        }

        private async Task<BackOfficeAccountIndexViewModel> BuildIndexModelAsync(BackOfficeAccountCreateInputViewModel? input = null)
        {
            var branchRecords = await _db.BranchRecords
                .AsNoTracking()
                .OrderByDescending(branch => branch.IsActive)
                .ThenBy(branch => branch.BranchId)
                .ToListAsync();

            var branchNameById = branchRecords.ToDictionary(
                branch => branch.BranchId,
                branch => branch.Name,
                StringComparer.OrdinalIgnoreCase);

            var branchOptions = branchRecords
                .Where(branch => branch.IsActive)
                .Select(branch => new BackOfficeBranchOptionViewModel
                {
                    BranchId = branch.BranchId,
                    BranchName = branch.Name
                })
                .ToList();

            var formInput = input ?? new BackOfficeAccountCreateInputViewModel();
            formInput.Role = NormalizeRole(formInput.Role) ?? ManagedRoles[0];
            formInput.BranchId = NormalizeBranchId(formInput.BranchId) ?? branchOptions.FirstOrDefault()?.BranchId ?? string.Empty;

            var roleOptions = ManagedRoles
                .Select(role => new BackOfficeRoleOptionViewModel
                {
                    Value = role,
                    Label = role
                })
                .ToList();

            var usersById = new Dictionary<string, IdentityUser>(StringComparer.Ordinal);
            var rolesByUserId = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            foreach (var role in ManagedRoles)
            {
                var usersInRole = await _userManager.GetUsersInRoleAsync(role);
                foreach (var roleUser in usersInRole)
                {
                    usersById[roleUser.Id] = roleUser;
                    if (!rolesByUserId.TryGetValue(roleUser.Id, out var roles))
                    {
                        roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        rolesByUserId[roleUser.Id] = roles;
                    }

                    roles.Add(role);
                }
            }

            var userIds = usersById.Keys.ToList();
            if (userIds.Count == 0)
            {
                return new BackOfficeAccountIndexViewModel
                {
                    CreateInput = formInput,
                    BranchOptions = branchOptions,
                    RoleOptions = roleOptions
                };
            }

            var branchByUserId = await _db.UserClaims
                .AsNoTracking()
                .Where(claim =>
                    claim.ClaimType == BranchAccess.BranchIdClaimType &&
                    userIds.Contains(claim.UserId))
                .GroupBy(claim => claim.UserId)
                .Select(group => new
                {
                    UserId = group.Key,
                    BranchId = group
                        .OrderByDescending(claim => claim.Id)
                        .Select(claim => claim.ClaimValue)
                        .FirstOrDefault()
                })
                .ToDictionaryAsync(
                    entry => entry.UserId,
                    entry => NormalizeBranchId(entry.BranchId),
                    StringComparer.Ordinal);

            var createdByUserIdByUser = await _db.UserClaims
                .AsNoTracking()
                .Where(claim =>
                    claim.ClaimType == BackOfficeCreatedByClaimType &&
                    userIds.Contains(claim.UserId))
                .GroupBy(claim => claim.UserId)
                .Select(group => new
                {
                    UserId = group.Key,
                    CreatedByUserId = group
                        .OrderByDescending(claim => claim.Id)
                        .Select(claim => claim.ClaimValue)
                        .FirstOrDefault()
                })
                .ToDictionaryAsync(
                    entry => entry.UserId,
                    entry => entry.CreatedByUserId,
                    StringComparer.Ordinal);

            var createdUtcByUserId = await _db.UserClaims
                .AsNoTracking()
                .Where(claim =>
                    claim.ClaimType == BackOfficeCreatedUtcClaimType &&
                    userIds.Contains(claim.UserId))
                .GroupBy(claim => claim.UserId)
                .Select(group => new
                {
                    UserId = group.Key,
                    CreatedUtcRaw = group
                        .OrderByDescending(claim => claim.Id)
                        .Select(claim => claim.ClaimValue)
                        .FirstOrDefault()
                })
                .ToDictionaryAsync(
                    entry => entry.UserId,
                    entry => ParseUtcClaim(entry.CreatedUtcRaw),
                    StringComparer.Ordinal);

            var statusByUserId = await _db.UserClaims
                .AsNoTracking()
                .Where(claim =>
                    claim.ClaimType == BackOfficeStatusClaimType &&
                    userIds.Contains(claim.UserId))
                .GroupBy(claim => claim.UserId)
                .Select(group => new
                {
                    UserId = group.Key,
                    StatusValue = group
                        .OrderByDescending(claim => claim.Id)
                        .Select(claim => claim.ClaimValue)
                        .FirstOrDefault()
                })
                .ToDictionaryAsync(
                    entry => entry.UserId,
                    entry => NormalizeStatusValue(entry.StatusValue),
                    StringComparer.Ordinal);

            var statusChangedByUserIdByUser = await _db.UserClaims
                .AsNoTracking()
                .Where(claim =>
                    claim.ClaimType == BackOfficeStatusChangedByClaimType &&
                    userIds.Contains(claim.UserId))
                .GroupBy(claim => claim.UserId)
                .Select(group => new
                {
                    UserId = group.Key,
                    ChangedByUserId = group
                        .OrderByDescending(claim => claim.Id)
                        .Select(claim => claim.ClaimValue)
                        .FirstOrDefault()
                })
                .ToDictionaryAsync(
                    entry => entry.UserId,
                    entry => entry.ChangedByUserId,
                    StringComparer.Ordinal);

            var statusChangedUtcByUserId = await _db.UserClaims
                .AsNoTracking()
                .Where(claim =>
                    claim.ClaimType == BackOfficeStatusChangedUtcClaimType &&
                    userIds.Contains(claim.UserId))
                .GroupBy(claim => claim.UserId)
                .Select(group => new
                {
                    UserId = group.Key,
                    ChangedUtcRaw = group
                        .OrderByDescending(claim => claim.Id)
                        .Select(claim => claim.ClaimValue)
                        .FirstOrDefault()
                })
                .ToDictionaryAsync(
                    entry => entry.UserId,
                    entry => ParseUtcClaim(entry.ChangedUtcRaw),
                    StringComparer.Ordinal);

            var creatorUserIds = createdByUserIdByUser.Values
                .Concat(statusChangedByUserIdByUser.Values)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var creatorEmailById = creatorUserIds.Count == 0
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : await _db.Users
                    .AsNoTracking()
                    .Where(user => creatorUserIds.Contains(user.Id))
                    .Select(user => new
                    {
                        user.Id,
                        Email = user.Email ?? user.UserName ?? user.Id
                    })
                    .ToDictionaryAsync(
                        entry => entry.Id,
                        entry => entry.Email,
                        StringComparer.Ordinal);

            var items = usersById.Values
                .Select(user =>
                {
                    rolesByUserId.TryGetValue(user.Id, out var roles);
                    roles ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    branchByUserId.TryGetValue(user.Id, out var branchId);
                    createdByUserIdByUser.TryGetValue(user.Id, out var createdByUserId);
                    createdUtcByUserId.TryGetValue(user.Id, out var createdUtc);
                    statusByUserId.TryGetValue(user.Id, out var statusValue);
                    statusChangedByUserIdByUser.TryGetValue(user.Id, out var statusChangedByUserId);
                    statusChangedUtcByUserId.TryGetValue(user.Id, out var statusChangedUtc);

                    var createdByDisplay = string.IsNullOrWhiteSpace(createdByUserId)
                        ? null
                        : creatorEmailById.TryGetValue(createdByUserId, out var creatorEmail)
                            ? creatorEmail
                            : createdByUserId;

                    var statusChangedByDisplay = string.IsNullOrWhiteSpace(statusChangedByUserId)
                        ? null
                        : creatorEmailById.TryGetValue(statusChangedByUserId, out var statusEditorEmail)
                            ? statusEditorEmail
                            : statusChangedByUserId;

                    var isActive = ResolveIsActive(statusValue, user);

                    return new BackOfficeAccountListItemViewModel
                    {
                        UserId = user.Id,
                        Email = user.Email ?? user.UserName ?? user.Id,
                        PhoneNumber = user.PhoneNumber,
                        RolesSummary = roles.Count == 0
                            ? "-"
                            : string.Join(", ", roles.OrderBy(role => role, StringComparer.OrdinalIgnoreCase)),
                        BranchId = branchId,
                        BranchName = !string.IsNullOrWhiteSpace(branchId) && branchNameById.TryGetValue(branchId, out var branchName)
                            ? branchName
                            : null,
                        CreatedUtc = createdUtc,
                        CreatedByUserId = createdByUserId,
                        CreatedByDisplay = createdByDisplay,
                        IsActive = isActive,
                        StatusChangedUtc = statusChangedUtc,
                        StatusChangedByUserId = statusChangedByUserId,
                        StatusChangedByDisplay = statusChangedByDisplay,
                        HasAuditRecord = createdUtc.HasValue ||
                            !string.IsNullOrWhiteSpace(createdByUserId) ||
                            statusChangedUtc.HasValue ||
                            !string.IsNullOrWhiteSpace(statusChangedByUserId)
                    };
                })
                .OrderBy(item => item.RolesSummary, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Email, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new BackOfficeAccountIndexViewModel
            {
                CreateInput = formInput,
                RoleOptions = roleOptions,
                BranchOptions = branchOptions,
                Accounts = items
            };
        }

        private static DateTime? ParseUtcClaim(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (!DateTime.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsed))
            {
                return null;
            }

            return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        }

        private static string? NormalizeStatusValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var normalized = value.Trim();
            if (string.Equals(normalized, BackOfficeStatusActiveValue, StringComparison.OrdinalIgnoreCase))
            {
                return BackOfficeStatusActiveValue;
            }

            if (string.Equals(normalized, BackOfficeStatusInactiveValue, StringComparison.OrdinalIgnoreCase))
            {
                return BackOfficeStatusInactiveValue;
            }

            return null;
        }

        private static bool ResolveIsActive(string? normalizedStatusValue, IdentityUser user)
        {
            if (string.Equals(normalizedStatusValue, BackOfficeStatusActiveValue, StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(normalizedStatusValue, BackOfficeStatusInactiveValue, StringComparison.Ordinal))
            {
                return false;
            }

            return !user.LockoutEnd.HasValue || user.LockoutEnd.Value <= DateTimeOffset.UtcNow;
        }

        private static string? NormalizeRole(string? role)
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                return null;
            }

            return ManagedRoles.FirstOrDefault(allowedRole =>
                string.Equals(allowedRole, role.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsManagedRole(string role)
        {
            return ManagedRoles.Any(allowedRole =>
                string.Equals(allowedRole, role, StringComparison.OrdinalIgnoreCase));
        }

        private static string? NormalizeBranchId(string? branchId)
        {
            if (string.IsNullOrWhiteSpace(branchId))
            {
                return null;
            }

            return branchId.Trim().ToUpperInvariant();
        }

        private void AddIdentityErrors(IdentityResult result)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }
    }
}
