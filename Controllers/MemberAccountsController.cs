using EJCFitnessGym.Data;
using EJCFitnessGym.Models;
using EJCFitnessGym.Models.Admin;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Services.AI;
using EJCFitnessGym.Services.Memberships;
using EJCFitnessGym.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EJCFitnessGym.Controllers
{
    [Authorize(Roles = "Admin,Finance,SuperAdmin")]
    [Route("Admin/MemberAccounts/[action]/{id?}")]
    public class MemberAccountsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IMemberSegmentationService _memberSegmentationService;
        private readonly IMemberAiInsightWriter _memberAiInsightWriter;
        private readonly IMemberChurnRiskService _memberChurnRiskService;

        public MemberAccountsController(
            ApplicationDbContext db,
            UserManager<IdentityUser> userManager,
            IMemberSegmentationService memberSegmentationService,
            IMemberAiInsightWriter memberAiInsightWriter,
            IMemberChurnRiskService memberChurnRiskService)
        {
            _db = db;
            _userManager = userManager;
            _memberSegmentationService = memberSegmentationService;
            _memberAiInsightWriter = memberAiInsightWriter;
            _memberChurnRiskService = memberChurnRiskService;
        }

        [HttpGet("/Admin/MemberAccounts")]
        public async Task<IActionResult> Index()
        {
            var utcNow = DateTime.UtcNow;
            var todayUtc = utcNow.Date;
            var currentBranchId = User.GetBranchId();
            var isSuperAdmin = User.IsInRole("SuperAdmin");
            if (!isSuperAdmin && string.IsNullOrWhiteSpace(currentBranchId))
            {
                return Forbid();
            }

            var memberUsers = (await _userManager.GetUsersInRoleAsync("Member")).ToList();
            var memberIds = memberUsers.Select(u => u.Id).ToList();
            var homeBranchByUserId = await MemberBranchAssignment.ResolveHomeBranchMapAsync(_db, memberIds);
            if (!isSuperAdmin)
            {
                var scopedMemberIds = memberIds
                    .Where(userId =>
                        homeBranchByUserId.TryGetValue(userId, out var branchId) &&
                        string.Equals(branchId, currentBranchId, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var scopedSet = scopedMemberIds.ToHashSet(StringComparer.Ordinal);
                memberUsers = memberUsers.Where(u => scopedSet.Contains(u.Id)).ToList();
                memberIds = memberUsers.Select(u => u.Id).ToList();
                homeBranchByUserId = homeBranchByUserId
                    .Where(entry => scopedSet.Contains(entry.Key))
                    .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
            }

            var profiles = await _db.MemberProfiles
                .Where(p => memberIds.Contains(p.UserId))
                .ToDictionaryAsync(p => p.UserId);

            var branchDisplayById = await _db.BranchRecords
                .AsNoTracking()
                .ToDictionaryAsync(
                    branch => branch.BranchId,
                    branch => BranchNaming.BuildDisplayName(branch.Name),
                    StringComparer.OrdinalIgnoreCase);

            var subscriptions = await _db.MemberSubscriptions
                .Where(s => memberIds.Contains(s.MemberUserId))
                .Include(s => s.SubscriptionPlan)
                .OrderByDescending(s => s.StartDateUtc)
                .ThenByDescending(s => s.Id)
                .ToListAsync();

            var successfulPayments = await (
                from invoice in _db.Invoices.AsNoTracking()
                join payment in _db.Payments.AsNoTracking()
                    on invoice.Id equals payment.InvoiceId
                where memberIds.Contains(invoice.MemberUserId) && payment.Status == PaymentStatus.Succeeded
                select new
                {
                    invoice.MemberUserId,
                    payment.Amount,
                    payment.PaidAtUtc
                })
                .ToListAsync();

            var overdueInvoiceCountsByUser = await _db.Invoices
                .AsNoTracking()
                .Where(invoice =>
                    memberIds.Contains(invoice.MemberUserId) &&
                    invoice.Status == InvoiceStatus.Overdue)
                .GroupBy(invoice => invoice.MemberUserId)
                .ToDictionaryAsync(
                    group => group.Key,
                    group => group.Count(),
                    StringComparer.Ordinal);

            var latestSubscriptionByUser = new Dictionary<string, MemberSubscription>();
            foreach (var subscription in subscriptions)
            {
                if (!latestSubscriptionByUser.ContainsKey(subscription.MemberUserId))
                {
                    latestSubscriptionByUser[subscription.MemberUserId] = subscription;
                }
            }

            var membershipMonthsByUser = subscriptions
                .GroupBy(s => s.MemberUserId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => CalculateMembershipMonths(
                        group.Min(s => s.StartDateUtc),
                        utcNow),
                    StringComparer.Ordinal);

            var paymentStatsByUser = successfulPayments
                .GroupBy(payment => payment.MemberUserId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => (
                        TotalSpending: (float)group.Sum(payment => payment.Amount),
                        BillingActivityCount: (float)group.Count(),
                        LastPaidAtUtc: (DateTime?)group.Max(payment => payment.PaidAtUtc)),
                    StringComparer.Ordinal);

            var items = memberUsers
                .Select(user =>
                {
                    profiles.TryGetValue(user.Id, out var profile);
                    latestSubscriptionByUser.TryGetValue(user.Id, out var subscription);

                    var fullName = BuildFullName(
                        profile?.FirstName,
                        profile?.LastName,
                        user.Email ?? user.UserName ?? user.Id);
                    var isMembershipActive = subscription is not null &&
                        subscription.Status == SubscriptionStatus.Active &&
                        (!subscription.EndDateUtc.HasValue || subscription.EndDateUtc.Value.Date >= todayUtc);

                    return new MemberAccountListItemViewModel
                    {
                        UserId = user.Id,
                        FullName = fullName,
                        Email = user.Email ?? string.Empty,
                        PhoneNumber = profile?.PhoneNumber ?? user.PhoneNumber,
                        HomeBranchId = homeBranchByUserId.TryGetValue(user.Id, out var homeBranchId) ? homeBranchId : null,
                        HomeBranchDisplayName = homeBranchByUserId.TryGetValue(user.Id, out var branchId) &&
                            !string.IsNullOrWhiteSpace(branchId) &&
                            branchDisplayById.TryGetValue(branchId, out var displayName)
                                ? displayName
                                : null,
                        PlanName = subscription?.SubscriptionPlan?.Name ?? "No Plan",
                        SubscriptionStatus = subscription?.Status,
                        StartDateUtc = subscription?.StartDateUtc,
                        EndDateUtc = subscription?.EndDateUtc,
                        IsMembershipActive = isMembershipActive
                    };
                })
                .OrderBy(m => m.FullName)
                .ToList();

            var segmentationInputs = items
                .Select(member =>
                {
                    paymentStatsByUser.TryGetValue(member.UserId, out var paymentStats);
                    membershipMonthsByUser.TryGetValue(member.UserId, out var membershipMonths);

                    return new MemberSegmentationInput
                    {
                        MemberUserId = member.UserId,
                        DisplayName = member.FullName,
                        TotalSpending = paymentStats.TotalSpending,
                        BillingActivityCount = paymentStats.BillingActivityCount,
                        MembershipMonths = membershipMonths
                    };
                })
                .ToList();

            var segmentation = _memberSegmentationService.SegmentMembers(segmentationInputs);
            if (isSuperAdmin)
            {
                _ = await _memberAiInsightWriter.PersistAsync(
                    segmentationInputs,
                    segmentation,
                    _userManager.GetUserId(User));
            }

            var churnRiskInputs = items
                .Select(member =>
                {
                    paymentStatsByUser.TryGetValue(member.UserId, out var paymentStats);
                    latestSubscriptionByUser.TryGetValue(member.UserId, out var subscription);
                    membershipMonthsByUser.TryGetValue(member.UserId, out var membershipMonths);
                    overdueInvoiceCountsByUser.TryGetValue(member.UserId, out var overdueInvoiceCount);

                    var hasActiveMembership = subscription is not null &&
                        subscription.Status == SubscriptionStatus.Active &&
                        (!subscription.EndDateUtc.HasValue || subscription.EndDateUtc.Value.Date >= todayUtc);

                    return new MemberChurnRiskInput
                    {
                        MemberUserId = member.UserId,
                        DisplayName = member.FullName,
                        TotalSpending = paymentStats.TotalSpending,
                        BillingActivityCount = paymentStats.BillingActivityCount,
                        MembershipMonths = membershipMonths,
                        DaysSinceLastSuccessfulPayment = paymentStats.LastPaidAtUtc.HasValue
                            ? (float?)(utcNow - paymentStats.LastPaidAtUtc.Value).TotalDays
                            : null,
                        DaysUntilMembershipEnd = subscription?.EndDateUtc.HasValue == true
                            ? (float?)(subscription.EndDateUtc.Value.Date - todayUtc).TotalDays
                            : null,
                        OverdueInvoiceCount = overdueInvoiceCount,
                        HasActiveMembership = hasActiveMembership
                    };
                })
                .ToList();

            var churnRisk = _memberChurnRiskService.PredictRisk(churnRiskInputs);

            var openRetentionActions = await _db.MemberRetentionActions
                .AsNoTracking()
                .Where(action =>
                    memberIds.Contains(action.MemberUserId) &&
                    (action.Status == MemberRetentionActionStatus.Open ||
                     action.Status == MemberRetentionActionStatus.InProgress))
                .GroupBy(action => action.MemberUserId)
                .Select(group => group
                    .OrderBy(action => action.Status)
                    .ThenBy(action => action.DueDateUtc ?? DateTime.MaxValue)
                    .First())
                .ToListAsync();

            var openRetentionByMemberId = openRetentionActions.ToDictionary(
                action => action.MemberUserId,
                action => action,
                StringComparer.Ordinal);

            foreach (var member in items)
            {
                if (segmentation.ResultsByMemberId.TryGetValue(member.UserId, out var segment))
                {
                    member.AiClusterId = segment.ClusterId;
                    member.AiSegmentLabel = segment.SegmentLabel;
                    member.AiSegmentDescription = segment.SegmentDescription;
                }

                if (churnRisk.ResultsByMemberId.TryGetValue(member.UserId, out var risk))
                {
                    member.AiChurnRiskScore = risk.RiskScore;
                    member.AiChurnRiskLevel = risk.RiskLevel;
                    member.AiChurnReasonSummary = risk.ReasonSummary;
                }

                if (openRetentionByMemberId.TryGetValue(member.UserId, out var retentionAction))
                {
                    member.HasOpenRetentionAction = true;
                    member.RetentionActionStatus = retentionAction.Status.ToString();
                    member.RetentionDueDateUtc = retentionAction.DueDateUtc;
                }
            }

            var model = new MemberAccountIndexViewModel
            {
                Members = items,
                SegmentedAtUtc = DateTime.UtcNow,
                ClusterSummary = segmentation.SegmentSummary
                    .Select(summary => new MemberAccountClusterSummaryItemViewModel
                    {
                        SegmentName = summary.SegmentLabel,
                        Description = summary.SegmentDescription,
                        MemberCount = summary.MemberCount
                    })
                    .ToList(),
                ChurnSummary = churnRisk.LevelSummary
                    .Select(summary => new MemberAccountChurnSummaryItemViewModel
                    {
                        RiskLevel = summary.RiskLevel,
                        MemberCount = summary.MemberCount
                    })
                    .ToList()
            };

            return View(model);
        }

        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> Create()
        {
            var model = new MemberAccountFormViewModel
            {
                RequirePassword = true,
                StartDateUtc = DateTime.UtcNow.Date,
                Status = SubscriptionStatus.Active
            };

            await PopulatePlanOptionsAsync(model.SubscriptionPlanId);
            await PopulateHomeBranchOptionsAsync(model.HomeBranchId);
            return View(model);
        }

        [Authorize(Roles = "SuperAdmin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(MemberAccountFormViewModel input)
        {
            input.RequirePassword = true;
            input.Email = (input.Email ?? string.Empty).Trim();
            input.PhoneNumber = string.IsNullOrWhiteSpace(input.PhoneNumber) ? null : input.PhoneNumber.Trim();
            input.FirstName = string.IsNullOrWhiteSpace(input.FirstName) ? null : input.FirstName.Trim();
            input.LastName = string.IsNullOrWhiteSpace(input.LastName) ? null : input.LastName.Trim();
            input.HomeBranchId = BranchNaming.NormalizeBranchId(input.HomeBranchId);

            if (string.IsNullOrWhiteSpace(input.Password))
            {
                ModelState.AddModelError(nameof(input.Password), "Password is required.");
            }

            if (input.EndDateUtc.HasValue && input.EndDateUtc.Value.Date < input.StartDateUtc.Date)
            {
                ModelState.AddModelError(nameof(input.EndDateUtc), "End date cannot be earlier than start date.");
            }

            var selectedPlan = await _db.SubscriptionPlans
                .FirstOrDefaultAsync(p => p.Id == input.SubscriptionPlanId && p.IsActive);
            if (selectedPlan is null)
            {
                ModelState.AddModelError(nameof(input.SubscriptionPlanId), "Selected plan is not available.");
            }

            if (!await IsValidActiveBranchAsync(input.HomeBranchId))
            {
                ModelState.AddModelError(nameof(input.HomeBranchId), "Select an active home branch.");
            }

            if (!ModelState.IsValid)
            {
                await PopulatePlanOptionsAsync(input.SubscriptionPlanId);
                await PopulateHomeBranchOptionsAsync(input.HomeBranchId);
                return View(input);
            }

            var existingUser = await _userManager.FindByEmailAsync(input.Email);
            if (existingUser is not null)
            {
                ModelState.AddModelError(nameof(input.Email), "A user with this email already exists.");
                await PopulatePlanOptionsAsync(input.SubscriptionPlanId);
                return View(input);
            }

            var user = new IdentityUser
            {
                UserName = input.Email,
                Email = input.Email,
                EmailConfirmed = true,
                PhoneNumber = input.PhoneNumber
            };

            await using var transaction = await _db.Database.BeginTransactionAsync();

            var createResult = await _userManager.CreateAsync(user, input.Password!);
            if (!createResult.Succeeded)
            {
                AddIdentityErrors(createResult);
                await PopulatePlanOptionsAsync(input.SubscriptionPlanId);
                await PopulateHomeBranchOptionsAsync(input.HomeBranchId);
                return View(input);
            }

            var roleResult = await _userManager.AddToRoleAsync(user, "Member");
            if (!roleResult.Succeeded)
            {
                AddIdentityErrors(roleResult);
                await PopulatePlanOptionsAsync(input.SubscriptionPlanId);
                await PopulateHomeBranchOptionsAsync(input.HomeBranchId);
                await PopulatePlanOptionsAsync(input.SubscriptionPlanId);
                return View(input);
            }

            var profile = new MemberProfile
            {
                UserId = user.Id,
                FirstName = input.FirstName,
                LastName = input.LastName,
                PhoneNumber = input.PhoneNumber,
                HomeBranchId = input.HomeBranchId,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };
            _db.MemberProfiles.Add(profile);

            await MemberBranchAssignment.AssignHomeBranchAsync(
                _db,
                _userManager,
                user,
                input.HomeBranchId,
                profile);

            _db.MemberSubscriptions.Add(new MemberSubscription
            {
                MemberUserId = user.Id,
                SubscriptionPlanId = input.SubscriptionPlanId,
                StartDateUtc = ToUtcDate(input.StartDateUtc),
                EndDateUtc = ToUtcDate(input.EndDateUtc),
                Status = input.Status
            });

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["StatusMessage"] = "Member account created successfully.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Details(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return NotFound();
            }

            if (!await CanAccessMemberAsync(id))
            {
                return Forbid();
            }

            var details = await BuildDetailsAsync(id);
            if (details is null)
            {
                return NotFound();
            }

            return View(details);
        }

        [Authorize(Roles = "Admin,Finance")]
        public async Task<IActionResult> Edit(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return NotFound();
            }

            if (!await CanAccessMemberAsync(id))
            {
                return Forbid();
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user is null || !await _userManager.IsInRoleAsync(user, "Member"))
            {
                return NotFound();
            }

            var profile = await _db.MemberProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
            var subscription = await _db.MemberSubscriptions
                .Where(s => s.MemberUserId == user.Id)
                .OrderByDescending(s => s.StartDateUtc)
                .ThenByDescending(s => s.Id)
                .FirstOrDefaultAsync();

            var model = new MemberAccountFormViewModel
            {
                UserId = user.Id,
                Email = user.Email ?? string.Empty,
                FirstName = profile?.FirstName,
                LastName = profile?.LastName,
                PhoneNumber = profile?.PhoneNumber ?? user.PhoneNumber,
                HomeBranchId = await MemberBranchAssignment.ResolveHomeBranchIdAsync(_db, user.Id) ?? string.Empty,
                SubscriptionPlanId = subscription?.SubscriptionPlanId ?? 0,
                StartDateUtc = (subscription?.StartDateUtc ?? DateTime.UtcNow).Date,
                EndDateUtc = subscription?.EndDateUtc?.Date,
                Status = subscription?.Status ?? SubscriptionStatus.Active,
                RequirePassword = false
            };

            await PopulatePlanOptionsAsync(model.SubscriptionPlanId);
            await PopulateHomeBranchOptionsAsync(model.HomeBranchId);
            return View(model);
        }

        [Authorize(Roles = "Admin,Finance")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(string id, MemberAccountFormViewModel input)
        {
            if (string.IsNullOrWhiteSpace(id) || id != input.UserId)
            {
                return BadRequest();
            }

            if (!await CanAccessMemberAsync(id))
            {
                return Forbid();
            }

            input.RequirePassword = false;
            input.Email = (input.Email ?? string.Empty).Trim();
            input.PhoneNumber = string.IsNullOrWhiteSpace(input.PhoneNumber) ? null : input.PhoneNumber.Trim();
            input.FirstName = string.IsNullOrWhiteSpace(input.FirstName) ? null : input.FirstName.Trim();
            input.LastName = string.IsNullOrWhiteSpace(input.LastName) ? null : input.LastName.Trim();
            input.HomeBranchId = BranchNaming.NormalizeBranchId(input.HomeBranchId);

            if (input.EndDateUtc.HasValue && input.EndDateUtc.Value.Date < input.StartDateUtc.Date)
            {
                ModelState.AddModelError(nameof(input.EndDateUtc), "End date cannot be earlier than start date.");
            }

            var selectedPlan = await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == input.SubscriptionPlanId);
            if (selectedPlan is null)
            {
                ModelState.AddModelError(nameof(input.SubscriptionPlanId), "Selected plan was not found.");
            }

            if (!await IsValidActiveBranchAsync(input.HomeBranchId))
            {
                ModelState.AddModelError(nameof(input.HomeBranchId), "Select an active home branch.");
            }

            if (!ModelState.IsValid)
            {
                await PopulatePlanOptionsAsync(input.SubscriptionPlanId);
                await PopulateHomeBranchOptionsAsync(input.HomeBranchId);
                return View(input);
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user is null || !await _userManager.IsInRoleAsync(user, "Member"))
            {
                return NotFound();
            }

            var emailOwner = await _userManager.FindByEmailAsync(input.Email);
            if (emailOwner is not null && !string.Equals(emailOwner.Id, user.Id, StringComparison.Ordinal))
            {
                ModelState.AddModelError(nameof(input.Email), "A different user already has this email.");
                await PopulatePlanOptionsAsync(input.SubscriptionPlanId);
                await PopulateHomeBranchOptionsAsync(input.HomeBranchId);
                return View(input);
            }

            user.Email = input.Email;
            user.UserName = input.Email;
            user.PhoneNumber = input.PhoneNumber;
            user.EmailConfirmed = true;

            var updateUserResult = await _userManager.UpdateAsync(user);
            if (!updateUserResult.Succeeded)
            {
                AddIdentityErrors(updateUserResult);
                await PopulatePlanOptionsAsync(input.SubscriptionPlanId);
                await PopulateHomeBranchOptionsAsync(input.HomeBranchId);
                return View(input);
            }

            if (!string.IsNullOrWhiteSpace(input.Password))
            {
                var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
                var resetResult = await _userManager.ResetPasswordAsync(user, resetToken, input.Password);
                if (!resetResult.Succeeded)
                {
                    AddIdentityErrors(resetResult);
                    await PopulatePlanOptionsAsync(input.SubscriptionPlanId);
                    await PopulateHomeBranchOptionsAsync(input.HomeBranchId);
                    return View(input);
                }
            }

            var profile = await _db.MemberProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
            if (profile is null)
            {
                profile = new MemberProfile
                {
                    UserId = user.Id,
                    CreatedUtc = DateTime.UtcNow
                };
                _db.MemberProfiles.Add(profile);
            }

            profile.FirstName = input.FirstName;
            profile.LastName = input.LastName;
            profile.PhoneNumber = input.PhoneNumber;
            profile.HomeBranchId = input.HomeBranchId;
            profile.UpdatedUtc = DateTime.UtcNow;

            await MemberBranchAssignment.AssignHomeBranchAsync(
                _db,
                _userManager,
                user,
                input.HomeBranchId,
                profile);

            var subscription = await _db.MemberSubscriptions
                .Where(s => s.MemberUserId == user.Id)
                .OrderByDescending(s => s.StartDateUtc)
                .ThenByDescending(s => s.Id)
                .FirstOrDefaultAsync();

            if (subscription is null)
            {
                subscription = new MemberSubscription
                {
                    MemberUserId = user.Id
                };
                _db.MemberSubscriptions.Add(subscription);
            }

            subscription.SubscriptionPlanId = input.SubscriptionPlanId;
            subscription.StartDateUtc = ToUtcDate(input.StartDateUtc);
            subscription.EndDateUtc = ToUtcDate(input.EndDateUtc);
            subscription.Status = input.Status;

            await _db.SaveChangesAsync();

            TempData["StatusMessage"] = "Member account updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Roles = "Admin,Finance")]
        public async Task<IActionResult> Delete(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return NotFound();
            }

            if (!await CanAccessMemberAsync(id))
            {
                return Forbid();
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user is null || !await _userManager.IsInRoleAsync(user, "Member"))
            {
                return NotFound();
            }

            var profile = await _db.MemberProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
            var subscription = await _db.MemberSubscriptions
                .Where(s => s.MemberUserId == user.Id)
                .Include(s => s.SubscriptionPlan)
                .OrderByDescending(s => s.StartDateUtc)
                .ThenByDescending(s => s.Id)
                .FirstOrDefaultAsync();

            var model = new MemberAccountDeleteViewModel
            {
                UserId = user.Id,
                FullName = BuildFullName(profile?.FirstName, profile?.LastName, user.Email ?? user.UserName ?? user.Id),
                Email = user.Email ?? string.Empty,
                PlanName = subscription?.SubscriptionPlan?.Name ?? "No Plan"
            };

            return View(model);
        }

        [Authorize(Roles = "Admin,Finance")]
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return NotFound();
            }

            if (!await CanAccessMemberAsync(id))
            {
                return Forbid();
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user is null || !await _userManager.IsInRoleAsync(user, "Member"))
            {
                return NotFound();
            }

            var deleteUserResult = await _userManager.DeleteAsync(user);
            if (!deleteUserResult.Succeeded)
            {
                TempData["StatusMessage"] = string.Join("; ", deleteUserResult.Errors.Select(e => e.Description));
                return RedirectToAction(nameof(Index));
            }

            var subscriptions = await _db.MemberSubscriptions
                .Where(s => s.MemberUserId == id)
                .ToListAsync();
            var profile = await _db.MemberProfiles.FirstOrDefaultAsync(p => p.UserId == id);

            if (subscriptions.Count > 0)
            {
                _db.MemberSubscriptions.RemoveRange(subscriptions);
            }

            if (profile is not null)
            {
                _db.MemberProfiles.Remove(profile);
            }

            await _db.SaveChangesAsync();

            TempData["StatusMessage"] = "Member account deleted.";
            return RedirectToAction(nameof(Index));
        }

        private async Task<MemberAccountDetailsViewModel?> BuildDetailsAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user is null || !await _userManager.IsInRoleAsync(user, "Member"))
            {
                return null;
            }

            var profile = await _db.MemberProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
            var subscription = await _db.MemberSubscriptions
                .Where(s => s.MemberUserId == user.Id)
                .Include(s => s.SubscriptionPlan)
                .OrderByDescending(s => s.StartDateUtc)
                .ThenByDescending(s => s.Id)
                .FirstOrDefaultAsync();
            var latestSegment = await _db.MemberSegmentSnapshots
                .AsNoTracking()
                .Where(snapshot => snapshot.MemberUserId == user.Id)
                .OrderByDescending(snapshot => snapshot.CapturedAtUtc)
                .FirstOrDefaultAsync();
            var openRetentionAction = await _db.MemberRetentionActions
                .AsNoTracking()
                .Where(action =>
                    action.MemberUserId == user.Id &&
                    (action.Status == MemberRetentionActionStatus.Open ||
                     action.Status == MemberRetentionActionStatus.InProgress))
                .OrderBy(action => action.Status)
                .ThenBy(action => action.DueDateUtc)
                .FirstOrDefaultAsync();
            var todayUtc = DateTime.UtcNow.Date;
            var isMembershipActive = subscription is not null &&
                subscription.Status == SubscriptionStatus.Active &&
                (!subscription.EndDateUtc.HasValue || subscription.EndDateUtc.Value.Date >= todayUtc);
            var homeBranchId = await MemberBranchAssignment.ResolveHomeBranchIdAsync(_db, user.Id);
            var homeBranchDisplayName = string.IsNullOrWhiteSpace(homeBranchId)
                ? null
                : await _db.BranchRecords
                    .AsNoTracking()
                    .Where(branch => branch.BranchId == homeBranchId)
                    .Select(branch => BranchNaming.BuildDisplayName(branch.Name))
                    .FirstOrDefaultAsync();

            return new MemberAccountDetailsViewModel
            {
                UserId = user.Id,
                FullName = BuildFullName(profile?.FirstName, profile?.LastName, user.Email ?? user.UserName ?? user.Id),
                Email = user.Email ?? string.Empty,
                PhoneNumber = profile?.PhoneNumber ?? user.PhoneNumber,
                HomeBranchId = homeBranchId,
                HomeBranchDisplayName = homeBranchDisplayName,
                PlanName = subscription?.SubscriptionPlan?.Name ?? "No Plan",
                SubscriptionStatus = subscription?.Status,
                StartDateUtc = subscription?.StartDateUtc,
                EndDateUtc = subscription?.EndDateUtc,
                IsMembershipActive = isMembershipActive,
                AiClusterId = latestSegment?.ClusterId is int clusterId ? (uint?)clusterId : null,
                AiSegmentLabel = latestSegment?.SegmentLabel,
                AiSegmentDescription = latestSegment?.SegmentDescription,
                AiSegmentCapturedAtUtc = latestSegment?.CapturedAtUtc,
                HasOpenRetentionAction = openRetentionAction is not null,
                RetentionActionStatus = openRetentionAction?.Status.ToString(),
                RetentionDueDateUtc = openRetentionAction?.DueDateUtc,
                RetentionReason = openRetentionAction?.Reason,
                RetentionSuggestedOffer = openRetentionAction?.SuggestedOffer
            };
        }

        private async Task<bool> CanAccessMemberAsync(string memberUserId)
        {
            if (User.IsInRole("SuperAdmin"))
            {
                return true;
            }

            var currentBranchId = User.GetBranchId();
            if (string.IsNullOrWhiteSpace(currentBranchId))
            {
                return false;
            }

            var memberHomeBranchId = await MemberBranchAssignment.ResolveHomeBranchIdAsync(_db, memberUserId);
            return string.Equals(memberHomeBranchId, currentBranchId, StringComparison.OrdinalIgnoreCase);
        }

        private async Task PopulatePlanOptionsAsync(int selectedPlanId)
        {
            var plans = await _db.SubscriptionPlans
                .OrderByDescending(p => p.IsActive)
                .ThenBy(p => p.Name)
                .ToListAsync();

            var options = plans
                .Where(p => p.IsActive || p.Id == selectedPlanId)
                .Select(p => new
                {
                    p.Id,
                    Label = $"{p.Name} (PHP {p.Price:0.00} • {p.BillingCycle})"
                })
                .ToList();

            ViewBag.PlanOptions = new SelectList(options, "Id", "Label", selectedPlanId);
        }

        private async Task PopulateHomeBranchOptionsAsync(string? selectedHomeBranchId)
        {
            var selectedBranchId = BranchNaming.NormalizeBranchId(selectedHomeBranchId);
            var branches = await _db.BranchRecords
                .AsNoTracking()
                .Where(branch => branch.IsActive)
                .OrderBy(branch => branch.BranchId)
                .Select(branch => new
                {
                    branch.BranchId,
                    Label = BranchNaming.BuildDisplayName(branch.Name)
                })
                .ToListAsync();

            ViewBag.HomeBranchOptions = new SelectList(branches, "BranchId", "Label", selectedBranchId);
        }

        private Task<bool> IsValidActiveBranchAsync(string? branchId)
        {
            var normalizedBranchId = BranchNaming.NormalizeBranchId(branchId);
            if (string.IsNullOrWhiteSpace(normalizedBranchId))
            {
                return Task.FromResult(false);
            }

            return _db.BranchRecords.AnyAsync(branch => branch.IsActive && branch.BranchId == normalizedBranchId);
        }

        private static DateTime ToUtcDate(DateTime date) => DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);

        private static DateTime? ToUtcDate(DateTime? date) =>
            date.HasValue ? DateTime.SpecifyKind(date.Value.Date, DateTimeKind.Utc) : null;

        private static string BuildFullName(string? firstName, string? lastName, string fallback)
        {
            var combined = $"{firstName} {lastName}".Trim();
            return string.IsNullOrWhiteSpace(combined) ? fallback : combined;
        }

        private static float CalculateMembershipMonths(DateTime startUtc, DateTime asOfUtc)
        {
            var normalizedStart = startUtc.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(startUtc, DateTimeKind.Utc)
                : startUtc.ToUniversalTime();

            var totalDays = Math.Max(0d, (asOfUtc.Date - normalizedStart.Date).TotalDays);
            return (float)(totalDays / 30.4375d);
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
