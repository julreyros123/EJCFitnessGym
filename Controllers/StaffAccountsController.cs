using System.Security.Claims;
using System.Security.Cryptography;
using System.Globalization;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Admin;
using EJCFitnessGym.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Controllers
{
    [Authorize(Roles = "Admin,SuperAdmin")]
    [Route("Admin/StaffAccounts/[action]/{id?}")]
    public class StaffAccountsController : Controller
    {
        private const string StaffPositionClaimType = "staff_position";
        private const string StaffArchiveStatusClaimType = "staff_archive_status";
        private const string StaffArchiveReasonClaimType = "staff_archive_reason";
        private const string StaffArchivedAtUtcClaimType = "staff_archived_at_utc";
        private const string StaffArchivedByUserIdClaimType = "staff_archived_by";
        private const string StaffArchiveStatusActiveValue = "active";
        private const string StaffArchiveStatusArchivedValue = "archived";
        private const string StaffEmailDomain = "gmail.com";
        private static readonly string[] StaffPositionOptions =
        {
            "Front Desk",
            "Coach",
            "Trainer",
            "Sales",
            "Maintenance"
        };

        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<StaffAccountsController> _logger;

        public StaffAccountsController(
            ApplicationDbContext db,
            UserManager<IdentityUser> userManager,
            IEmailSender emailSender,
            ILogger<StaffAccountsController> logger)
        {
            _db = db;
            _userManager = userManager;
            _emailSender = emailSender;
            _logger = logger;
        }

        [HttpGet("/Admin/StaffAccounts")]
        public async Task<IActionResult> Index()
        {
            var isSuperAdmin = User.IsInRole("SuperAdmin");
            var currentBranchId = NormalizeBranchId(User.GetBranchId());
            if (!isSuperAdmin && string.IsNullOrWhiteSpace(currentBranchId))
            {
                return Forbid();
            }

            var model = await BuildIndexModelAsync();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(StaffAccountCreateInputViewModel input)
        {
            var isSuperAdmin = User.IsInRole("SuperAdmin");
            var currentBranchId = NormalizeBranchId(User.GetBranchId());
            if (!isSuperAdmin && string.IsNullOrWhiteSpace(currentBranchId))
            {
                return Forbid();
            }

            var emailValidation = NormalizeStaffEmail(input.Email);
            input.Email = emailValidation.Email ?? string.Empty;
            input.PhoneNumber = NormalizePhilippinePhoneNumber(input.PhoneNumber);
            input.Position = NormalizePosition(input.Position) ?? string.Empty;
            input.BranchId = isSuperAdmin
                ? NormalizeBranchId(input.BranchId)
                : currentBranchId;

            if (!emailValidation.IsValid)
            {
                ModelState.AddModelError(nameof(input.Email), emailValidation.ErrorMessage ?? "Email is invalid.");
            }

            if (!string.IsNullOrWhiteSpace(input.PhoneNumber) && input.PhoneNumber.Length != 13)
            {
                ModelState.AddModelError(nameof(input.PhoneNumber), "Phone number must be a valid PH mobile number.");
            }

            if (string.IsNullOrWhiteSpace(input.Position))
            {
                ModelState.AddModelError(nameof(input.Position), "Position is required.");
            }
            else if (!IsSupportedPosition(input.Position))
            {
                ModelState.AddModelError(nameof(input.Position), "Selected position is invalid.");
            }

            if (string.IsNullOrWhiteSpace(input.BranchId))
            {
                ModelState.AddModelError(nameof(input.BranchId), "Branch is required.");
            }
            else
            {
                var hasActiveBranch = await _db.BranchRecords
                    .AsNoTracking()
                    .AnyAsync(b => b.IsActive && b.BranchId == input.BranchId);
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

            var generatedPassword = GenerateSecurePassword();
            var createResult = await _userManager.CreateAsync(user, generatedPassword);
            if (!createResult.Succeeded)
            {
                AddIdentityErrors(createResult);
                var createFailedModel = await BuildIndexModelAsync(input);
                return View(nameof(Index), createFailedModel);
            }

            var roleResult = await _userManager.AddToRoleAsync(user, "Staff");
            if (!roleResult.Succeeded)
            {
                await _userManager.DeleteAsync(user);
                AddIdentityErrors(roleResult);
                var roleFailedModel = await BuildIndexModelAsync(input);
                return View(nameof(Index), roleFailedModel);
            }

            var branchClaimResult = await _userManager.AddClaimAsync(
                user,
                new Claim(BranchAccess.BranchIdClaimType, input.BranchId!));
            if (!branchClaimResult.Succeeded)
            {
                await _userManager.RemoveFromRoleAsync(user, "Staff");
                await _userManager.DeleteAsync(user);
                AddIdentityErrors(branchClaimResult);
                var claimFailedModel = await BuildIndexModelAsync(input);
                return View(nameof(Index), claimFailedModel);
            }

            var positionClaimResult = await _userManager.AddClaimAsync(
                user,
                new Claim(StaffPositionClaimType, input.Position));
            if (!positionClaimResult.Succeeded)
            {
                await _userManager.RemoveFromRoleAsync(user, "Staff");
                await _userManager.DeleteAsync(user);
                AddIdentityErrors(positionClaimResult);
                var positionClaimFailedModel = await BuildIndexModelAsync(input);
                return View(nameof(Index), positionClaimFailedModel);
            }

            var emailSent = await TrySendStaffCredentialsEmailAsync(
                input.Email,
                generatedPassword,
                input.Position,
                input.BranchId);

            TempData["StatusMessage"] = emailSent
                ? "Staff account created. Login credentials were sent to Gmail."
                : "Staff account created, but credential email was not sent. Check SMTP settings.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdatePosition(string userId, string position)
        {
            var isSuperAdmin = User.IsInRole("SuperAdmin");
            var currentBranchId = NormalizeBranchId(User.GetBranchId());
            if (!isSuperAdmin && string.IsNullOrWhiteSpace(currentBranchId))
            {
                return Forbid();
            }

            if (string.IsNullOrWhiteSpace(userId))
            {
                TempData["StatusMessage"] = "Staff account was not specified.";
                return RedirectToAction(nameof(Index));
            }

            var normalizedPosition = NormalizePosition(position) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedPosition) || !IsSupportedPosition(normalizedPosition))
            {
                TempData["StatusMessage"] = "Selected position is invalid.";
                return RedirectToAction(nameof(Index));
            }

            var targetUser = await _userManager.FindByIdAsync(userId);
            if (targetUser is null || !await _userManager.IsInRoleAsync(targetUser, "Staff"))
            {
                TempData["StatusMessage"] = "Staff account was not found.";
                return RedirectToAction(nameof(Index));
            }

            if (!isSuperAdmin)
            {
                var targetBranchId = await _db.UserClaims
                    .AsNoTracking()
                    .Where(claim =>
                        claim.UserId == targetUser.Id &&
                        claim.ClaimType == BranchAccess.BranchIdClaimType &&
                        claim.ClaimValue != null)
                    .OrderByDescending(claim => claim.Id)
                    .Select(claim => claim.ClaimValue)
                    .FirstOrDefaultAsync();
                if (!string.Equals(NormalizeBranchId(targetBranchId), currentBranchId, StringComparison.OrdinalIgnoreCase))
                {
                    return Forbid();
                }
            }

            var existingClaims = await _userManager.GetClaimsAsync(targetUser);
            var positionClaims = existingClaims
                .Where(claim => claim.Type == StaffPositionClaimType)
                .ToList();
            var currentPosition = positionClaims
                .Select(claim => NormalizePosition(claim.Value))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

            if (string.Equals(currentPosition, normalizedPosition, StringComparison.OrdinalIgnoreCase))
            {
                TempData["StatusMessage"] = $"Position is already set to {normalizedPosition}.";
                return RedirectToAction(nameof(Index));
            }

            if (positionClaims.Count > 0)
            {
                var removeClaimsResult = await _userManager.RemoveClaimsAsync(targetUser, positionClaims);
                if (!removeClaimsResult.Succeeded)
                {
                    TempData["StatusMessage"] = "Could not update staff position. Please try again.";
                    return RedirectToAction(nameof(Index));
                }
            }

            var addClaimResult = await _userManager.AddClaimAsync(
                targetUser,
                new Claim(StaffPositionClaimType, normalizedPosition));
            if (!addClaimResult.Succeeded)
            {
                TempData["StatusMessage"] = "Could not update staff position. Please try again.";
                return RedirectToAction(nameof(Index));
            }

            TempData["StatusMessage"] = $"Staff position updated to {normalizedPosition}.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Archive(string userId, string? reason)
        {
            var isSuperAdmin = User.IsInRole("SuperAdmin");
            var currentBranchId = NormalizeBranchId(User.GetBranchId());
            if (!isSuperAdmin && string.IsNullOrWhiteSpace(currentBranchId))
            {
                return Forbid();
            }

            if (string.IsNullOrWhiteSpace(userId))
            {
                TempData["StatusMessage"] = "Staff account was not specified.";
                return RedirectToAction(nameof(Index));
            }

            var actorUserId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return Challenge();
            }

            var targetUser = await _userManager.FindByIdAsync(userId);
            if (targetUser is null || !await _userManager.IsInRoleAsync(targetUser, "Staff"))
            {
                TempData["StatusMessage"] = "Staff account was not found.";
                return RedirectToAction(nameof(Index));
            }

            if (string.Equals(targetUser.Id, actorUserId, StringComparison.Ordinal))
            {
                TempData["StatusMessage"] = "You cannot archive your own account.";
                return RedirectToAction(nameof(Index));
            }

            if (!isSuperAdmin)
            {
                var targetBranchId = await _db.UserClaims
                    .AsNoTracking()
                    .Where(claim =>
                        claim.UserId == targetUser.Id &&
                        claim.ClaimType == BranchAccess.BranchIdClaimType &&
                        claim.ClaimValue != null)
                    .OrderByDescending(claim => claim.Id)
                    .Select(claim => claim.ClaimValue)
                    .FirstOrDefaultAsync();

                if (!string.Equals(NormalizeBranchId(targetBranchId), currentBranchId, StringComparison.OrdinalIgnoreCase))
                {
                    return Forbid();
                }
            }

            var currentArchiveStatus = await _db.UserClaims
                .AsNoTracking()
                .Where(claim =>
                    claim.UserId == targetUser.Id &&
                    claim.ClaimType == StaffArchiveStatusClaimType &&
                    claim.ClaimValue != null)
                .OrderByDescending(claim => claim.Id)
                .Select(claim => claim.ClaimValue)
                .FirstOrDefaultAsync();

            if (IsArchivedStatus(currentArchiveStatus))
            {
                TempData["StatusMessage"] = "Staff account is already archived.";
                return RedirectToAction(nameof(Index));
            }

            if (!targetUser.LockoutEnabled)
            {
                var enableLockoutResult = await _userManager.SetLockoutEnabledAsync(targetUser, true);
                if (!enableLockoutResult.Succeeded)
                {
                    TempData["StatusMessage"] = "Could not archive staff account. Please try again.";
                    return RedirectToAction(nameof(Index));
                }
            }

            var lockoutResult = await _userManager.SetLockoutEndDateAsync(targetUser, DateTimeOffset.MaxValue);
            if (!lockoutResult.Succeeded)
            {
                TempData["StatusMessage"] = "Could not archive staff account. Please try again.";
                return RedirectToAction(nameof(Index));
            }

            var archiveReason = string.IsNullOrWhiteSpace(reason)
                ? "Archived from staff directory."
                : reason.Trim();

            var nowUtc = DateTime.UtcNow;
            var claimsResult = await _userManager.AddClaimsAsync(
                targetUser,
                new[]
                {
                    new Claim(StaffArchiveStatusClaimType, StaffArchiveStatusArchivedValue),
                    new Claim(StaffArchiveReasonClaimType, archiveReason),
                    new Claim(StaffArchivedAtUtcClaimType, nowUtc.ToString("O", CultureInfo.InvariantCulture)),
                    new Claim(StaffArchivedByUserIdClaimType, actorUserId)
                });

            if (!claimsResult.Succeeded)
            {
                TempData["StatusMessage"] = "Staff account archived, but archive history could not be recorded.";
                return RedirectToAction(nameof(Index));
            }

            TempData["StatusMessage"] = $"Staff account '{targetUser.Email ?? targetUser.UserName ?? targetUser.Id}' archived successfully.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Restore(string userId)
        {
            var isSuperAdmin = User.IsInRole("SuperAdmin");
            var currentBranchId = NormalizeBranchId(User.GetBranchId());
            if (!isSuperAdmin && string.IsNullOrWhiteSpace(currentBranchId))
            {
                return Forbid();
            }

            if (string.IsNullOrWhiteSpace(userId))
            {
                TempData["StatusMessage"] = "Staff account was not specified.";
                return RedirectToAction(nameof(Index));
            }

            var actorUserId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return Challenge();
            }

            var targetUser = await _userManager.FindByIdAsync(userId);
            if (targetUser is null || !await _userManager.IsInRoleAsync(targetUser, "Staff"))
            {
                TempData["StatusMessage"] = "Staff account was not found.";
                return RedirectToAction(nameof(Index));
            }

            if (!isSuperAdmin)
            {
                var targetBranchId = await _db.UserClaims
                    .AsNoTracking()
                    .Where(claim =>
                        claim.UserId == targetUser.Id &&
                        claim.ClaimType == BranchAccess.BranchIdClaimType &&
                        claim.ClaimValue != null)
                    .OrderByDescending(claim => claim.Id)
                    .Select(claim => claim.ClaimValue)
                    .FirstOrDefaultAsync();

                if (!string.Equals(NormalizeBranchId(targetBranchId), currentBranchId, StringComparison.OrdinalIgnoreCase))
                {
                    return Forbid();
                }
            }

            var currentArchiveStatus = await _db.UserClaims
                .AsNoTracking()
                .Where(claim =>
                    claim.UserId == targetUser.Id &&
                    claim.ClaimType == StaffArchiveStatusClaimType &&
                    claim.ClaimValue != null)
                .OrderByDescending(claim => claim.Id)
                .Select(claim => claim.ClaimValue)
                .FirstOrDefaultAsync();

            if (!IsArchivedStatus(currentArchiveStatus))
            {
                TempData["StatusMessage"] = "Staff account is already active.";
                return RedirectToAction(nameof(Index));
            }

            var unlockResult = await _userManager.SetLockoutEndDateAsync(targetUser, null);
            if (!unlockResult.Succeeded)
            {
                TempData["StatusMessage"] = "Could not restore staff account. Please try again.";
                return RedirectToAction(nameof(Index));
            }

            _ = await _userManager.ResetAccessFailedCountAsync(targetUser);

            var nowUtc = DateTime.UtcNow;
            var claimsResult = await _userManager.AddClaimsAsync(
                targetUser,
                new[]
                {
                    new Claim(StaffArchiveStatusClaimType, StaffArchiveStatusActiveValue),
                    new Claim(StaffArchiveReasonClaimType, "Restored from archive."),
                    new Claim(StaffArchivedAtUtcClaimType, nowUtc.ToString("O", CultureInfo.InvariantCulture)),
                    new Claim(StaffArchivedByUserIdClaimType, actorUserId)
                });

            if (!claimsResult.Succeeded)
            {
                TempData["StatusMessage"] = "Staff account restored, but archive history could not be recorded.";
                return RedirectToAction(nameof(Index));
            }

            TempData["StatusMessage"] = $"Staff account '{targetUser.Email ?? targetUser.UserName ?? targetUser.Id}' restored successfully.";
            return RedirectToAction(nameof(Index));
        }

        private async Task<StaffAccountIndexViewModel> BuildIndexModelAsync(StaffAccountCreateInputViewModel? input = null)
        {
            var isSuperAdmin = User.IsInRole("SuperAdmin");
            var currentBranchId = NormalizeBranchId(User.GetBranchId());

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
                .Where(branch => isSuperAdmin || string.Equals(branch.BranchId, currentBranchId, StringComparison.OrdinalIgnoreCase))
                .Select(branch => new StaffBranchOptionViewModel
                {
                    BranchId = branch.BranchId,
                    BranchName = branch.Name
                })
                .ToList();

            var staffUsers = (await _userManager.GetUsersInRoleAsync("Staff")).ToList();
            var staffUserIds = staffUsers.Select(user => user.Id).ToList();

            if (!isSuperAdmin && !string.IsNullOrWhiteSpace(currentBranchId))
            {
                var scopedStaffUserIds = await _db.UserClaims
                    .AsNoTracking()
                    .Where(claim =>
                        claim.ClaimType == BranchAccess.BranchIdClaimType &&
                        claim.ClaimValue == currentBranchId &&
                        staffUserIds.Contains(claim.UserId))
                    .Select(claim => claim.UserId)
                    .Distinct()
                    .ToListAsync();

                var scopedStaffSet = scopedStaffUserIds.ToHashSet(StringComparer.Ordinal);
                staffUsers = staffUsers.Where(user => scopedStaffSet.Contains(user.Id)).ToList();
                staffUserIds = staffUsers.Select(user => user.Id).ToList();
            }

            var branchByUserId = await _db.UserClaims
                .AsNoTracking()
                .Where(claim =>
                    claim.ClaimType == BranchAccess.BranchIdClaimType &&
                    staffUserIds.Contains(claim.UserId))
                .GroupBy(claim => claim.UserId)
                .Select(group => new
                {
                    UserId = group.Key,
                    BranchId = group
                        .OrderByDescending(claim => claim.Id)
                        .Select(claim => claim.ClaimValue)
                        .FirstOrDefault()
                })
                .ToDictionaryAsync(entry => entry.UserId, entry => NormalizeBranchId(entry.BranchId), StringComparer.Ordinal);

            var positionByUserId = await _db.UserClaims
                .AsNoTracking()
                .Where(claim =>
                    claim.ClaimType == StaffPositionClaimType &&
                    staffUserIds.Contains(claim.UserId))
                .GroupBy(claim => claim.UserId)
                .Select(group => new
                {
                    UserId = group.Key,
                    Position = group
                        .OrderByDescending(claim => claim.Id)
                        .Select(claim => claim.ClaimValue)
                        .FirstOrDefault()
                })
                .ToDictionaryAsync(entry => entry.UserId, entry => NormalizePosition(entry.Position), StringComparer.Ordinal);

            var archiveStatusByUserId = await _db.UserClaims
                .AsNoTracking()
                .Where(claim =>
                    claim.ClaimType == StaffArchiveStatusClaimType &&
                    staffUserIds.Contains(claim.UserId))
                .GroupBy(claim => claim.UserId)
                .Select(group => new
                {
                    UserId = group.Key,
                    ArchiveStatus = group
                        .OrderByDescending(claim => claim.Id)
                        .Select(claim => claim.ClaimValue)
                        .FirstOrDefault()
                })
                .ToDictionaryAsync(entry => entry.UserId, entry => entry.ArchiveStatus, StringComparer.Ordinal);

            var archiveReasonByUserId = await _db.UserClaims
                .AsNoTracking()
                .Where(claim =>
                    claim.ClaimType == StaffArchiveReasonClaimType &&
                    staffUserIds.Contains(claim.UserId))
                .GroupBy(claim => claim.UserId)
                .Select(group => new
                {
                    UserId = group.Key,
                    ArchiveReason = group
                        .OrderByDescending(claim => claim.Id)
                        .Select(claim => claim.ClaimValue)
                        .FirstOrDefault()
                })
                .ToDictionaryAsync(entry => entry.UserId, entry => entry.ArchiveReason, StringComparer.Ordinal);

            var archivedAtByUserId = await _db.UserClaims
                .AsNoTracking()
                .Where(claim =>
                    claim.ClaimType == StaffArchivedAtUtcClaimType &&
                    staffUserIds.Contains(claim.UserId))
                .GroupBy(claim => claim.UserId)
                .Select(group => new
                {
                    UserId = group.Key,
                    ArchivedAt = group
                        .OrderByDescending(claim => claim.Id)
                        .Select(claim => claim.ClaimValue)
                        .FirstOrDefault()
                })
                .ToDictionaryAsync(entry => entry.UserId, entry => ParseUtcDateTime(entry.ArchivedAt), StringComparer.Ordinal);

            var formInput = input ?? new StaffAccountCreateInputViewModel();
            formInput.Position = NormalizePosition(formInput.Position) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(formInput.Position))
            {
                formInput.Position = StaffPositionOptions[0];
            }

            if (!isSuperAdmin)
            {
                formInput.BranchId = currentBranchId;
            }
            else if (string.IsNullOrWhiteSpace(formInput.BranchId))
            {
                formInput.BranchId = branchOptions.FirstOrDefault()?.BranchId;
            }

            var allStaffAccounts = staffUsers
                .Select(user =>
                {
                    branchByUserId.TryGetValue(user.Id, out var branchId);
                    positionByUserId.TryGetValue(user.Id, out var position);
                    archiveStatusByUserId.TryGetValue(user.Id, out var archiveStatus);
                    archiveReasonByUserId.TryGetValue(user.Id, out var archiveReason);
                    archivedAtByUserId.TryGetValue(user.Id, out var archivedAtUtc);
                    string? resolvedBranchName = null;
                    var hasBranchName = !string.IsNullOrWhiteSpace(branchId) &&
                        branchNameById.TryGetValue(branchId, out resolvedBranchName);

                    return new StaffAccountListItemViewModel
                    {
                        UserId = user.Id,
                        Email = user.Email ?? user.UserName ?? user.Id,
                        PhoneNumber = user.PhoneNumber,
                        BranchId = branchId,
                        BranchName = hasBranchName ? resolvedBranchName : null,
                        Position = position,
                        IsArchived = IsArchivedStatus(archiveStatus),
                        ArchiveReason = string.IsNullOrWhiteSpace(archiveReason) ? null : archiveReason.Trim(),
                        ArchivedAtUtc = archivedAtUtc
                    };
                })
                .OrderBy(item => item.Email, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var activeStaffAccounts = allStaffAccounts
                .Where(item => !item.IsArchived)
                .ToList();

            var archivedStaffAccounts = allStaffAccounts
                .Where(item => item.IsArchived)
                .ToList();

            var model = new StaffAccountIndexViewModel
            {
                IsSuperAdmin = isSuperAdmin,
                CanChooseBranch = isSuperAdmin,
                DefaultBranchId = currentBranchId,
                DefaultBranchName = string.IsNullOrWhiteSpace(currentBranchId)
                    ? null
                    : branchNameById.TryGetValue(currentBranchId, out var branchName)
                        ? branchName
                        : null,
                CreateInput = formInput,
                BranchOptions = branchOptions,
                PositionOptions = StaffPositionOptions
                    .Select(position => new StaffPositionOptionViewModel
                    {
                        Value = position,
                        Label = position
                    })
                    .ToList(),
                StaffAccounts = activeStaffAccounts,
                ActiveStaffAccounts = activeStaffAccounts,
                ArchivedStaffAccounts = archivedStaffAccounts
            };

            return model;
        }

        private static bool IsArchivedStatus(string? statusValue)
        {
            return string.Equals(
                statusValue?.Trim(),
                StaffArchiveStatusArchivedValue,
                StringComparison.OrdinalIgnoreCase);
        }

        private static DateTime? ParseUtcDateTime(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed)
                ? parsed
                : null;
        }

        private static string? NormalizeBranchId(string? branchId)
        {
            if (string.IsNullOrWhiteSpace(branchId))
            {
                return null;
            }

            return branchId.Trim().ToUpperInvariant();
        }

        private static string? NormalizePosition(string? position)
        {
            if (string.IsNullOrWhiteSpace(position))
            {
                return null;
            }

            return position.Trim();
        }

        private static string? NormalizePhilippinePhoneNumber(string? rawPhone)
        {
            if (string.IsNullOrWhiteSpace(rawPhone))
            {
                return null;
            }

            var digits = new string(rawPhone.Where(char.IsDigit).ToArray());
            if (digits.StartsWith("63", StringComparison.Ordinal))
            {
                digits = digits[2..];
            }

            if (digits.StartsWith("0", StringComparison.Ordinal))
            {
                digits = digits[1..];
            }

            if (digits.Length == 10)
            {
                return $"+63{digits}";
            }

            return rawPhone.Trim();
        }

        private static string GenerateSecurePassword(int length = 14)
        {
            const string lower = "abcdefghijkmnopqrstuvwxyz";
            const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
            const string digits = "23456789";
            const string symbols = "@#$%!*-_+?";

            if (length < 12)
            {
                length = 12;
            }

            var chars = new List<char>(length)
            {
                lower[RandomNumberGenerator.GetInt32(lower.Length)],
                upper[RandomNumberGenerator.GetInt32(upper.Length)],
                digits[RandomNumberGenerator.GetInt32(digits.Length)],
                symbols[RandomNumberGenerator.GetInt32(symbols.Length)]
            };

            var all = $"{lower}{upper}{digits}{symbols}";
            for (var i = chars.Count; i < length; i++)
            {
                chars.Add(all[RandomNumberGenerator.GetInt32(all.Length)]);
            }

            for (var index = chars.Count - 1; index > 0; index--)
            {
                var swapIndex = RandomNumberGenerator.GetInt32(index + 1);
                (chars[index], chars[swapIndex]) = (chars[swapIndex], chars[index]);
            }

            return new string(chars.ToArray());
        }

        private static (bool IsValid, string? Email, string? ErrorMessage) NormalizeStaffEmail(string? rawEmail)
        {
            if (string.IsNullOrWhiteSpace(rawEmail))
            {
                return (false, null, "Email is required.");
            }

            var value = rawEmail.Trim().ToLowerInvariant();
            if (value.EndsWith($"@{StaffEmailDomain}", StringComparison.Ordinal))
            {
                value = value[..^($"@{StaffEmailDomain}".Length)];
            }

            var localPart = value.Contains('@', StringComparison.Ordinal)
                ? value.Split('@', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                : value;

            if (string.IsNullOrWhiteSpace(localPart))
            {
                return (false, null, "Enter a valid Gmail username.");
            }

            var sanitizedLocalPart = new string(localPart
                .Where(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-' or '+' or '%')
                .ToArray());

            if (string.IsNullOrWhiteSpace(sanitizedLocalPart))
            {
                return (false, null, "Enter a valid Gmail username.");
            }

            var email = $"{sanitizedLocalPart}@{StaffEmailDomain}";
            return (true, email, null);
        }

        private async Task<bool> TrySendStaffCredentialsEmailAsync(
            string email,
            string generatedPassword,
            string position,
            string? branchId)
        {
            try
            {
                var backOfficeLoginUrl = Url.Page(
                    "/Account/BackOfficeLogin",
                    pageHandler: null,
                    values: new { area = "Identity" },
                    protocol: Request.Scheme);

                if (string.IsNullOrWhiteSpace(backOfficeLoginUrl))
                {
                    backOfficeLoginUrl = $"{Request.Scheme}://{Request.Host}/Identity/Account/BackOfficeLogin";
                }

                var encodedEmail = System.Net.WebUtility.HtmlEncode(email);
                var encodedPassword = System.Net.WebUtility.HtmlEncode(generatedPassword);
                var encodedPosition = System.Net.WebUtility.HtmlEncode(position);
                var encodedBranch = System.Net.WebUtility.HtmlEncode(branchId ?? "-");
                var encodedLoginUrl = System.Net.WebUtility.HtmlEncode(backOfficeLoginUrl);

                var subject = "EJC Fitness Gym - Staff Account Credentials";
                var htmlMessage =
                    "<p>Your staff account has been created.</p>" +
                    $"<p><strong>Email:</strong> {encodedEmail}<br/>" +
                    $"<strong>Temporary Password:</strong> {encodedPassword}<br/>" +
                    $"<strong>Position:</strong> {encodedPosition}<br/>" +
                    $"<strong>Branch:</strong> {encodedBranch}</p>" +
                    $"<p>Back office login: <a href=\"{encodedLoginUrl}\">{encodedLoginUrl}</a></p>" +
                    "<p>Please sign in and change your password immediately.</p>";

                await _emailSender.SendEmailAsync(email, subject, htmlMessage);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send staff account credentials email to {Email}.", email);
                return false;
            }
        }

        private static bool IsSupportedPosition(string position)
        {
            return StaffPositionOptions.Any(option =>
                string.Equals(option, position, StringComparison.OrdinalIgnoreCase));
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
