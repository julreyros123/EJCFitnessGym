using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Admin;
using EJCFitnessGym.Models;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Models.Finance;
using EJCFitnessGym.Models.Integration;
using EJCFitnessGym.Models.Member;
using EJCFitnessGym.Services.AI;
using EJCFitnessGym.Services.Memberships;
using EJCFitnessGym.Services.Payments;
using EJCFitnessGym.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;

namespace EJCFitnessGym.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp"
        };
        private const string MembershipCancellationActionType = "Membership Cancellation";

        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IWebHostEnvironment _environment;
        private readonly IMemberChurnRiskService _memberChurnRiskService;
        private readonly IPayMongoMembershipReconciliationService? _payMongoMembershipReconciliationService;
        private readonly ILogger<DashboardController>? _logger;

        public DashboardController(
            ApplicationDbContext db,
            UserManager<IdentityUser> userManager,
            IWebHostEnvironment environment,
            IMemberChurnRiskService memberChurnRiskService,
            IPayMongoMembershipReconciliationService? payMongoMembershipReconciliationService = null,
            ILogger<DashboardController>? logger = null)
        {
            _db = db;
            _userManager = userManager;
            _environment = environment;
            _memberChurnRiskService = memberChurnRiskService;
            _payMongoMembershipReconciliationService = payMongoMembershipReconciliationService;
            _logger = logger;
        }

        public IActionResult Index()
        {
            if (User.IsInRole("SuperAdmin"))
            {
                return RedirectToAction(nameof(SuperAdmin));
            }

            if (User.IsInRole("Admin"))
            {
                return RedirectToPage("/Admin/Dashboard");
            }

            if (User.IsInRole("Finance"))
            {
                return RedirectToPage("/Finance/Dashboard");
            }

            if (User.IsInRole("Staff"))
            {
                return RedirectToPage("/Staff/CheckIn");
            }

            if (User.IsInRole("Member"))
            {
                return RedirectToAction(nameof(Member));
            }

            return RedirectToAction(nameof(Member));
        }

        [Authorize(Roles = "SuperAdmin")]
        public async Task<IActionResult> SuperAdmin()
        {
            var utcNow = DateTime.UtcNow;
            var monthStartUtc = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var monthEndUtc = monthStartUtc.AddMonths(1);
            var todayUtc = utcNow.Date;

            var users = await _userManager.Users
                .AsNoTracking()
                .Select(u => new
                {
                    u.Id,
                    Email = u.Email ?? u.UserName ?? u.Id
                })
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
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(name => name)
                        .ToList(),
                    StringComparer.Ordinal);

            var branchClaims = await _db.UserClaims
                .AsNoTracking()
                .Where(c => c.ClaimType == BranchAccess.BranchIdClaimType && c.ClaimValue != null)
                .Select(c => new
                {
                    c.UserId,
                    c.ClaimValue,
                    c.Id
                })
                .ToListAsync();

            var branchByUser = branchClaims
                .GroupBy(c => c.UserId, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => NormalizeBranchId(g
                        .OrderByDescending(x => x.Id)
                        .Select(x => x.ClaimValue)
                        .FirstOrDefault()),
                    StringComparer.Ordinal);

            var activeCatalogBranchCount = await _db.BranchRecords
                .AsNoTracking()
                .CountAsync(b => b.IsActive);

            var trackedRoles = new[] { "SuperAdmin", "Admin", "Finance", "Staff", "Member" };
            var roleCounts = trackedRoles
                .Select(roleName => new SuperAdminRoleCountItemViewModel
                {
                    RoleName = roleName,
                    UserCount = rolePairs.Count(x => string.Equals(x.RoleName, roleName, StringComparison.OrdinalIgnoreCase))
                })
                .ToList();

            var memberUserIds = rolePairs
                .Where(pair => string.Equals(pair.RoleName, "Member", StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.UserId)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var userEmailById = users.ToDictionary(user => user.Id, user => user.Email, StringComparer.Ordinal);

            var latestSubscriptions = await _db.MemberSubscriptions
                .AsNoTracking()
                .Where(subscription => memberUserIds.Contains(subscription.MemberUserId))
                .GroupBy(s => s.MemberUserId)
                .Select(g => g
                    .OrderByDescending(s => s.StartDateUtc)
                    .ThenByDescending(s => s.Id)
                    .Select(s => new
                    {
                        s.MemberUserId,
                        s.Status,
                        s.EndDateUtc
                    })
                    .First())
                .ToListAsync();

            var latestSubscriptionByUserId = latestSubscriptions.ToDictionary(
                subscription => subscription.MemberUserId,
                subscription => subscription,
                StringComparer.Ordinal);

            var activeMemberCount = latestSubscriptions.Count(s =>
                s.Status == SubscriptionStatus.Active &&
                (!s.EndDateUtc.HasValue || s.EndDateUtc.Value.Date >= todayUtc));

            var memberTenureByUserId = await _db.MemberSubscriptions
                .AsNoTracking()
                .Where(subscription => memberUserIds.Contains(subscription.MemberUserId))
                .GroupBy(subscription => subscription.MemberUserId)
                .Select(group => new
                {
                    MemberUserId = group.Key,
                    FirstStartUtc = group.Min(subscription => subscription.StartDateUtc)
                })
                .ToDictionaryAsync(
                    item => item.MemberUserId,
                    item => (float)Math.Max(0d, (utcNow.Date - item.FirstStartUtc.Date).TotalDays / 30.4375d),
                    StringComparer.Ordinal);

            var successfulMemberPayments = await (
                from invoice in _db.Invoices.AsNoTracking()
                join payment in _db.Payments.AsNoTracking()
                    on invoice.Id equals payment.InvoiceId
                where memberUserIds.Contains(invoice.MemberUserId) && payment.Status == PaymentStatus.Succeeded
                select new
                {
                    invoice.MemberUserId,
                    payment.Amount,
                    payment.PaidAtUtc
                })
                .ToListAsync();

            var paymentStatsByUserId = successfulMemberPayments
                .GroupBy(payment => payment.MemberUserId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => (
                        TotalSpending: (float)group.Sum(payment => payment.Amount),
                        BillingActivityCount: (float)group.Count(),
                        LastPaidAtUtc: (DateTime?)group.Max(payment => payment.PaidAtUtc)),
                    StringComparer.Ordinal);

            var overdueInvoiceCountByUserId = await _db.Invoices
                .AsNoTracking()
                .Where(invoice =>
                    memberUserIds.Contains(invoice.MemberUserId) &&
                    invoice.Status == InvoiceStatus.Overdue)
                .GroupBy(invoice => invoice.MemberUserId)
                .ToDictionaryAsync(
                    group => group.Key,
                    group => group.Count(),
                    StringComparer.Ordinal);

            var churnInputs = memberUserIds
                .Select(memberUserId =>
                {
                    latestSubscriptionByUserId.TryGetValue(memberUserId, out var subscription);
                    paymentStatsByUserId.TryGetValue(memberUserId, out var paymentStats);
                    memberTenureByUserId.TryGetValue(memberUserId, out var membershipMonths);
                    overdueInvoiceCountByUserId.TryGetValue(memberUserId, out var overdueInvoiceCount);

                    var hasActiveMembership = subscription is not null &&
                        subscription.Status == SubscriptionStatus.Active &&
                        (!subscription.EndDateUtc.HasValue || subscription.EndDateUtc.Value.Date >= todayUtc);

                    return new MemberChurnRiskInput
                    {
                        MemberUserId = memberUserId,
                        DisplayName = userEmailById.TryGetValue(memberUserId, out var emailValue) ? emailValue : memberUserId,
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

            var churnRisk = _memberChurnRiskService.PredictRisk(churnInputs);
            var highChurnRiskCount = churnRisk.LevelSummary
                .FirstOrDefault(item => string.Equals(item.RiskLevel, "High", StringComparison.OrdinalIgnoreCase))
                ?.MemberCount ?? 0;
            var mediumChurnRiskCount = churnRisk.LevelSummary
                .FirstOrDefault(item => string.Equals(item.RiskLevel, "Medium", StringComparison.OrdinalIgnoreCase))
                ?.MemberCount ?? 0;

            var atRiskMembers = churnRisk.ResultsByMemberId.Values
                .Where(result =>
                    string.Equals(result.RiskLevel, "High", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(result.RiskLevel, "Medium", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(result => result.RiskScore)
                .ThenBy(result => result.MemberUserId, StringComparer.Ordinal)
                .Take(10)
                .Select(result =>
                {
                    var email = userEmailById.TryGetValue(result.MemberUserId, out var memberEmail)
                        ? memberEmail
                        : result.MemberUserId;
                    paymentStatsByUserId.TryGetValue(result.MemberUserId, out var paymentStats);

                    return new SuperAdminChurnRiskMemberItemViewModel
                    {
                        MemberEmail = email,
                        RiskScore = result.RiskScore,
                        RiskLevel = result.RiskLevel,
                        ReasonSummary = result.ReasonSummary,
                        LastPaymentUtc = paymentStats.LastPaidAtUtc
                    };
                })
                .ToList();

            var privilegedRoles = new HashSet<string>(new[] { "SuperAdmin", "Admin", "Finance", "Staff" }, StringComparer.OrdinalIgnoreCase);
            var branchRequiredRoles = new HashSet<string>(new[] { "Admin", "Finance", "Staff" }, StringComparer.OrdinalIgnoreCase);

            var privilegedUsers = users
                .Select(user =>
                {
                    rolesByUser.TryGetValue(user.Id, out var roles);
                    roles ??= new List<string>();

                    var userPrivilegedRoles = roles
                        .Where(role => privilegedRoles.Contains(role))
                        .ToList();

                    if (userPrivilegedRoles.Count == 0)
                    {
                        return null;
                    }

                    branchByUser.TryGetValue(user.Id, out var branchId);
                    var requiresBranch = userPrivilegedRoles.Any(role => branchRequiredRoles.Contains(role));

                    return new SuperAdminPrivilegedUserItemViewModel
                    {
                        Email = user.Email,
                        RolesSummary = string.Join(", ", userPrivilegedRoles),
                        BranchId = branchId,
                        RequiresBranch = requiresBranch,
                        MissingBranch = requiresBranch && string.IsNullOrWhiteSpace(branchId)
                    };
                })
                .Where(item => item is not null)
                .Cast<SuperAdminPrivilegedUserItemViewModel>()
                .OrderBy(item => item.Email, StringComparer.OrdinalIgnoreCase)
                .Take(120)
                .ToList();

            var branchLoads = branchByUser.Values
                .Where(branchId => !string.IsNullOrWhiteSpace(branchId))
                .Select(branchId => branchId!)
                .GroupBy(branchId => branchId, StringComparer.OrdinalIgnoreCase)
                .Select(g => new SuperAdminBranchLoadItemViewModel
                {
                    BranchId = g.Key,
                    UserCount = g.Count()
                })
                .OrderByDescending(item => item.UserCount)
                .ThenBy(item => item.BranchId, StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();

            var latestSegmentSnapshots = await _db.MemberSegmentSnapshots
                .AsNoTracking()
                .GroupBy(snapshot => snapshot.MemberUserId)
                .Select(group => group
                    .OrderByDescending(snapshot => snapshot.CapturedAtUtc)
                    .ThenByDescending(snapshot => snapshot.Id)
                    .First())
                .ToListAsync();

            var memberSegmentDistribution = latestSegmentSnapshots
                .GroupBy(
                    snapshot => string.IsNullOrWhiteSpace(snapshot.SegmentLabel) ? "Unclassified" : snapshot.SegmentLabel,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => new SuperAdminMemberSegmentDistributionItemViewModel
                {
                    SegmentLabel = group.Key,
                    MemberCount = group.Count()
                })
                .OrderByDescending(item => item.MemberCount)
                .ThenBy(item => item.SegmentLabel, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var revenueThisMonth = await _db.Payments
                .AsNoTracking()
                .Where(p =>
                    p.Status == PaymentStatus.Succeeded &&
                    p.PaidAtUtc >= monthStartUtc &&
                    p.PaidAtUtc < monthEndUtc)
                .SumAsync(p => (decimal?)p.Amount) ?? 0m;

            var openInvoiceQuery = _db.Invoices
                .AsNoTracking()
                .Where(i => i.Status == InvoiceStatus.Unpaid || i.Status == InvoiceStatus.Overdue);

            var openInvoiceCount = await openInvoiceQuery.CountAsync();
            var openInvoiceAmount = await openInvoiceQuery.SumAsync(i => (decimal?)i.Amount) ?? 0m;

            var newAlertCount = await _db.FinanceAlertLogs
                .AsNoTracking()
                .CountAsync(l => l.State == FinanceAlertState.New);

            var acknowledgedAlertCount = await _db.FinanceAlertLogs
                .AsNoTracking()
                .CountAsync(l => l.State == FinanceAlertState.Acknowledged);

            var recentAlerts = await _db.FinanceAlertLogs
                .AsNoTracking()
                .OrderByDescending(l => l.CreatedUtc)
                .Take(8)
                .Select(l => new SuperAdminRecentAlertItemViewModel
                {
                    CreatedUtc = l.CreatedUtc,
                    AlertType = l.AlertType,
                    Severity = l.Severity,
                    State = l.State.ToString(),
                    Message = l.Message
                })
                .ToListAsync();

            var failedOutboxCount = await _db.IntegrationOutboxMessages
                .AsNoTracking()
                .CountAsync(m => m.Status == IntegrationOutboxStatus.Failed);

            var pendingOutboxCount = await _db.IntegrationOutboxMessages
                .AsNoTracking()
                .CountAsync(m => m.Status == IntegrationOutboxStatus.Pending || m.Status == IntegrationOutboxStatus.Processing);

            var oldestPendingOutboxUtc = await _db.IntegrationOutboxMessages
                .AsNoTracking()
                .Where(m => m.Status == IntegrationOutboxStatus.Pending || m.Status == IntegrationOutboxStatus.Processing)
                .OrderBy(m => m.CreatedUtc)
                .Select(m => (DateTime?)m.CreatedUtc)
                .FirstOrDefaultAsync();

            var openRetentionActionsQuery = _db.MemberRetentionActions
                .AsNoTracking()
                .Where(action =>
                    action.Status == MemberRetentionActionStatus.Open ||
                    action.Status == MemberRetentionActionStatus.InProgress);

            var openRetentionActionCount = await openRetentionActionsQuery.CountAsync();

            var openRetentionActions = await openRetentionActionsQuery
                .OrderBy(action => action.DueDateUtc ?? DateTime.MaxValue)
                .ThenByDescending(action => action.CreatedUtc)
                .Take(10)
                .ToListAsync();

            var retentionMemberIds = openRetentionActions
                .Select(action => action.MemberUserId)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var retentionMemberEmailById = await _userManager.Users
                .AsNoTracking()
                .Where(user => retentionMemberIds.Contains(user.Id))
                .Select(user => new
                {
                    user.Id,
                    Email = user.Email ?? user.UserName ?? user.Id
                })
                .ToDictionaryAsync(
                    item => item.Id,
                    item => item.Email,
                    StringComparer.Ordinal);

            var retentionActions = openRetentionActions
                .Select(action => new SuperAdminRetentionActionItemViewModel
                {
                    MemberEmail = retentionMemberEmailById.TryGetValue(action.MemberUserId, out var email)
                        ? email
                        : action.MemberUserId,
                    ActionType = action.ActionType,
                    SegmentLabel = action.SegmentLabel,
                    Status = action.Status.ToString(),
                    Reason = action.Reason,
                    DueDateUtc = action.DueDateUtc
                })
                .ToList();

            var model = new SuperAdminDashboardViewModel
            {
                AsOfUtc = utcNow,
                RevenuePeriodLabel = monthStartUtc.ToString("MMMM yyyy"),
                TotalUserCount = users.Count,
                TotalMemberCount = roleCounts.FirstOrDefault(x => string.Equals(x.RoleName, "Member", StringComparison.OrdinalIgnoreCase))?.UserCount ?? 0,
                ActiveMemberCount = activeMemberCount,
                ActiveBranchCount = activeCatalogBranchCount > 0 ? activeCatalogBranchCount : branchLoads.Count,
                RevenueThisMonth = revenueThisMonth,
                OpenInvoiceCount = openInvoiceCount,
                OpenInvoiceAmount = openInvoiceAmount,
                NewAlertCount = newAlertCount,
                AcknowledgedAlertCount = acknowledgedAlertCount,
                FailedOutboxCount = failedOutboxCount,
                PendingOutboxCount = pendingOutboxCount,
                OldestPendingOutboxUtc = oldestPendingOutboxUtc,
                UnassignedBackOfficeCount = privilegedUsers.Count(u => u.MissingBranch),
                OpenRetentionActionCount = openRetentionActionCount,
                HighChurnRiskCount = highChurnRiskCount,
                MediumChurnRiskCount = mediumChurnRiskCount,
                RoleCounts = roleCounts,
                BranchLoads = branchLoads,
                PrivilegedUsers = privilegedUsers,
                RecentAlerts = recentAlerts,
                MemberSegmentDistribution = memberSegmentDistribution,
                RetentionActions = retentionActions,
                AtRiskMembers = atRiskMembers
            };

            return View(model);
        }

        [Authorize]
        public async Task<IActionResult> Member(CancellationToken cancellationToken)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null)
            {
                return Challenge();
            }

            if (_payMongoMembershipReconciliationService is not null)
            {
                try
                {
                    await _payMongoMembershipReconciliationService
                        .ReconcilePendingMemberPaymentsAsync(user.Id, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(
                        ex,
                        "PayMongo member payment reconciliation failed for user {UserId}.",
                        user.Id);
                }
            }

            var memberCheckInCode = await EnsureMemberCheckInCodeAsync(user, cancellationToken);

            var profile = await _db.MemberProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == user.Id, cancellationToken);

            var currentSubscription = await _db.MemberSubscriptions
                .AsNoTracking()
                .Where(s => s.MemberUserId == user.Id)
                .Include(s => s.SubscriptionPlan)
                .OrderBy(s => s.Status == SubscriptionStatus.Active
                    ? 0
                    : s.Status == SubscriptionStatus.Paused
                        ? 1
                        : 2)
                .ThenByDescending(s => s.EndDateUtc ?? DateTime.MinValue)
                .ThenByDescending(s => s.StartDateUtc)
                .ThenByDescending(s => s.Id)
                .FirstOrDefaultAsync(cancellationToken);

            var invoicesQuery = _db.Invoices
                .AsNoTracking()
                .Where(i => i.MemberUserId == user.Id);

            var lifetimeSpend = await invoicesQuery
                .Where(i => i.Status == InvoiceStatus.Paid)
                .SumAsync(i => (decimal?)i.Amount, cancellationToken) ?? 0m;

            var nowUtc = DateTime.UtcNow;

            var outstandingBalance = await invoicesQuery
                .Where(i =>
                    i.Status == InvoiceStatus.Overdue ||
                    (i.Status == InvoiceStatus.Unpaid && i.DueDateUtc <= nowUtc))
                .SumAsync(i => (decimal?)i.Amount, cancellationToken) ?? 0m;

            var scheduledBalance = await invoicesQuery
                .Where(i => i.Status == InvoiceStatus.Unpaid && i.DueDateUtc > nowUtc)
                .SumAsync(i => (decimal?)i.Amount, cancellationToken) ?? 0m;

            var totalInvoices = await invoicesQuery.CountAsync(cancellationToken);
            var paidInvoiceCount = await invoicesQuery.CountAsync(i => i.Status == InvoiceStatus.Paid, cancellationToken);
            var pendingInvoiceCount = await invoicesQuery.CountAsync(
                i => i.Status == InvoiceStatus.Unpaid && i.DueDateUtc <= nowUtc,
                cancellationToken);
            var expiredInvoiceCount = await invoicesQuery.CountAsync(i => i.Status == InvoiceStatus.Overdue, cancellationToken);
            var upcomingInvoiceCount = await invoicesQuery.CountAsync(
                i => i.Status == InvoiceStatus.Unpaid && i.DueDateUtc > nowUtc,
                cancellationToken);
            var openInvoiceCount = pendingInvoiceCount + expiredInvoiceCount;

            var nextOpenInvoiceDueDateUtc = await invoicesQuery
                .Where(i => i.Status == InvoiceStatus.Unpaid || i.Status == InvoiceStatus.Overdue)
                .OrderBy(i => i.DueDateUtc)
                .Select(i => (DateTime?)i.DueDateUtc)
                .FirstOrDefaultAsync(cancellationToken);

            var nextPaymentDueDateUtc = nextOpenInvoiceDueDateUtc ?? currentSubscription?.EndDateUtc;

            var memberDisplayName = BuildDisplayName(
                profile?.FirstName,
                profile?.LastName,
                user.Email ?? user.UserName ?? "Member");

            var (statusLabel, statusBadgeClass, hasActiveMembership) = ResolveMembershipStatus(currentSubscription);
            var recentActivities = await _db.IntegrationOutboxMessages
                .AsNoTracking()
                .Where(message =>
                    message.Target == Models.Integration.IntegrationOutboxTarget.User &&
                    message.TargetValue == user.Id)
                .OrderByDescending(message => message.CreatedUtc)
                .ThenByDescending(message => message.Id)
                .Take(12)
                .Select(message => new MemberActivityItemViewModel
                {
                    EventUtc = message.CreatedUtc,
                    EventType = message.EventType,
                    Title = ToMemberActivityTitle(message.EventType),
                    Message = message.Message
                })
                .ToListAsync(cancellationToken);

            var model = new MemberDashboardViewModel
            {
                MemberDisplayName = memberDisplayName,
                CurrentPlanName = currentSubscription?.SubscriptionPlan?.Name ?? "No plan selected",
                MembershipStatusLabel = statusLabel,
                MembershipStatusBadgeClass = statusBadgeClass,
                HasSubscriptionRecord = currentSubscription is not null,
                HasActiveMembership = hasActiveMembership,
                MembershipStartDateUtc = currentSubscription?.StartDateUtc,
                MembershipEndDateUtc = currentSubscription?.EndDateUtc,
                NextPaymentDueDateUtc = nextPaymentDueDateUtc,
                LifetimeSpend = lifetimeSpend,
                OutstandingBalance = outstandingBalance,
                ScheduledBalance = scheduledBalance,
                TotalInvoices = totalInvoices,
                PaidInvoiceCount = paidInvoiceCount,
                OpenInvoiceCount = openInvoiceCount,
                PendingInvoiceCount = pendingInvoiceCount,
                ExpiredInvoiceCount = expiredInvoiceCount,
                UpcomingInvoiceCount = upcomingInvoiceCount,
                ProfileCompletionPercent = CalculateCompletionPercent(profile),
                MemberCheckInCode = memberCheckInCode,
                MemberQrPayload = MemberCheckIn.BuildQrPayload(memberCheckInCode),
                RecentActivities = recentActivities
            };

            return View(model);
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null)
            {
                return Challenge();
            }

            var input = await BuildProfileInputAsync(user);
            await PopulateHomeBranchOptionsAsync(input.HomeBranchId);
            return View(input);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Profile(MemberProfileInputModel input)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null)
            {
                return Challenge();
            }

            input.Email = user.Email;
            input.Bmi = CalculateBmi(input.HeightCm, input.WeightKg);

            if (input.ProfileImage != null)
            {
                var extension = Path.GetExtension(input.ProfileImage.FileName);
                if (!AllowedImageExtensions.Contains(extension))
                {
                    ModelState.AddModelError(nameof(input.ProfileImage), "Only .jpg, .jpeg, .png, and .webp files are allowed.");
                }

                if (input.ProfileImage.Length > 2 * 1024 * 1024)
                {
                    ModelState.AddModelError(nameof(input.ProfileImage), "Image must be 2 MB or smaller.");
                }
            }

            if (!ModelState.IsValid)
            {
                var existing = await _db.MemberProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == user.Id);
                input.Bmi = input.Bmi ?? existing?.Bmi;
                input.CompletionPercent = CalculateCompletionPercentFromInput(input, existing?.ProfileImagePath);
                input.ExistingImagePath = existing?.ProfileImagePath;
                await PopulateMembershipSettingsAsync(user.Id, input);
                await PopulateHomeBranchOptionsAsync(input.HomeBranchId);
                return View(input);
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

            profile.FirstName = input.FirstName?.Trim();
            profile.LastName = input.LastName?.Trim();
            profile.Age = input.Age;
            profile.PhoneNumber = input.PhoneNumber?.Trim();
            profile.HeightCm = input.HeightCm;
            profile.WeightKg = input.WeightKg;
            profile.Bmi = CalculateBmi(input.HeightCm, input.WeightKg);
            profile.UpdatedUtc = DateTime.UtcNow;
            profile.HomeBranchId = BranchNaming.NormalizeBranchId(input.HomeBranchId);

            if (input.ProfileImage != null)
            {
                var uploadRoot = Path.Combine(_environment.WebRootPath, "uploads", "profiles");
                Directory.CreateDirectory(uploadRoot);

                var extension = Path.GetExtension(input.ProfileImage.FileName).ToLowerInvariant();
                var fileName = $"{Guid.NewGuid():N}{extension}";
                var outputPath = Path.Combine(uploadRoot, fileName);

                await using (var stream = System.IO.File.Create(outputPath))
                {
                    await input.ProfileImage.CopyToAsync(stream);
                }

                if (!string.IsNullOrWhiteSpace(profile.ProfileImagePath))
                {
                    var oldPath = Path.Combine(_environment.WebRootPath, profile.ProfileImagePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                    if (System.IO.File.Exists(oldPath))
                    {
                        System.IO.File.Delete(oldPath);
                    }
                }

                profile.ProfileImagePath = $"/uploads/profiles/{fileName}";
            }

            await MemberBranchAssignment.AssignHomeBranchAsync(
                _db,
                _userManager,
                user,
                input.HomeBranchId,
                profile);

            await _db.SaveChangesAsync();
            TempData["StatusMessage"] = "Profile updated successfully.";
            return RedirectToAction(nameof(Profile));
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestMembershipCancellation(MemberCancellationRequestInputModel input)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null)
            {
                return Challenge();
            }

            var reason = (input.CancellationReason ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(reason))
            {
                ModelState.AddModelError(nameof(MemberProfileInputModel.CancellationReason), "Please state your reason before submitting cancellation.");
            }
            else if (reason.Length < 15)
            {
                ModelState.AddModelError(nameof(MemberProfileInputModel.CancellationReason), "Please enter at least 15 characters for your cancellation reason.");
            }
            else if (reason.Length > 300)
            {
                ModelState.AddModelError(nameof(MemberProfileInputModel.CancellationReason), "Cancellation reason must be 300 characters or fewer.");
            }

            if (!ModelState.IsValid)
            {
                var viewModel = await BuildProfileInputAsync(user, reason);
                return View(nameof(Profile), viewModel);
            }

            var existingOpenRequest = await _db.MemberRetentionActions
                .AsNoTracking()
                .Where(action =>
                    action.MemberUserId == user.Id &&
                    action.ActionType == MembershipCancellationActionType &&
                    (action.Status == MemberRetentionActionStatus.Open || action.Status == MemberRetentionActionStatus.InProgress))
                .OrderByDescending(action => action.CreatedUtc)
                .FirstOrDefaultAsync();

            if (existingOpenRequest is not null)
            {
                TempData["MembershipStatusMessage"] = "A cancellation request is already pending review.";
                TempData["MembershipStatusType"] = "warning";
                return RedirectToAction(nameof(Profile));
            }

            var nowUtc = DateTime.UtcNow;
            _db.MemberRetentionActions.Add(new MemberRetentionAction
            {
                MemberUserId = user.Id,
                ActionType = MembershipCancellationActionType,
                SegmentLabel = "Member Request",
                Reason = reason,
                Status = MemberRetentionActionStatus.Open,
                DueDateUtc = nowUtc.AddDays(3),
                CreatedUtc = nowUtc,
                UpdatedUtc = nowUtc,
                CreatedByUserId = user.Id,
                UpdatedByUserId = user.Id,
                Notes = "Submitted from member profile settings."
            });

            await _db.SaveChangesAsync();

            TempData["MembershipStatusMessage"] = "Cancellation request submitted. Our team will review your reason before finalizing.";
            TempData["MembershipStatusType"] = "success";
            return RedirectToAction(nameof(Profile));
        }

        private async Task<MemberProfileInputModel> BuildProfileInputAsync(IdentityUser user, string? cancellationReason = null)
        {
            var profile = await _db.MemberProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == user.Id);

            var input = new MemberProfileInputModel
            {
                Email = user.Email,
                FirstName = profile?.FirstName,
                LastName = profile?.LastName,
                Age = profile?.Age,
                PhoneNumber = profile?.PhoneNumber,
                HeightCm = profile?.HeightCm,
                WeightKg = profile?.WeightKg,
                Bmi = profile?.Bmi,
                HomeBranchId = profile?.HomeBranchId ?? await MemberBranchAssignment.ResolveHomeBranchIdAsync(_db, user.Id),
                CompletionPercent = CalculateCompletionPercent(profile),
                ExistingImagePath = profile?.ProfileImagePath,
                CancellationReason = cancellationReason
            };

            await PopulateMembershipSettingsAsync(user.Id, input);
            return input;
        }

        private async Task PopulateMembershipSettingsAsync(string userId, MemberProfileInputModel input)
        {
            var currentSubscription = await _db.MemberSubscriptions
                .AsNoTracking()
                .Where(subscription => subscription.MemberUserId == userId)
                .Include(subscription => subscription.SubscriptionPlan)
                .OrderBy(subscription => subscription.Status == SubscriptionStatus.Active
                    ? 0
                    : subscription.Status == SubscriptionStatus.Paused
                        ? 1
                        : 2)
                .ThenByDescending(subscription => subscription.EndDateUtc ?? DateTime.MinValue)
                .ThenByDescending(subscription => subscription.StartDateUtc)
                .ThenByDescending(subscription => subscription.Id)
                .FirstOrDefaultAsync();

            var (statusLabel, statusBadgeClass, hasActiveMembership) = ResolveMembershipStatus(currentSubscription);
            input.CurrentPlanName = currentSubscription?.SubscriptionPlan?.Name ?? "No plan selected";
            input.MembershipStatusLabel = statusLabel;
            input.MembershipStatusBadgeClass = statusBadgeClass;
            input.HasActiveMembership = hasActiveMembership;

            var openCancellationRequest = await _db.MemberRetentionActions
                .AsNoTracking()
                .Where(action =>
                    action.MemberUserId == userId &&
                    action.ActionType == MembershipCancellationActionType &&
                    (action.Status == MemberRetentionActionStatus.Open || action.Status == MemberRetentionActionStatus.InProgress))
                .OrderByDescending(action => action.CreatedUtc)
                .FirstOrDefaultAsync();

            input.HasOpenCancellationRequest = openCancellationRequest is not null;
            input.OpenCancellationRequestedUtc = openCancellationRequest?.CreatedUtc;
            input.OpenCancellationReason = openCancellationRequest?.Reason;
        }

        private async Task PopulateHomeBranchOptionsAsync(string? selectedBranchId)
        {
            var normalizedSelectedBranchId = NormalizeBranchId(selectedBranchId);
            var branchOptions = await _db.BranchRecords
                .AsNoTracking()
                .Where(branch => branch.IsActive)
                .OrderBy(branch => branch.BranchId)
                .Select(branch => new
                {
                    branch.BranchId,
                    Label = BranchNaming.BuildDisplayName(branch.Name)
                })
                .ToListAsync();

            ViewBag.HomeBranchOptions = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(
                branchOptions,
                "BranchId",
                "Label",
                normalizedSelectedBranchId);
        }

        private static (string Label, string BadgeClass, bool HasActiveMembership) ResolveMembershipStatus(MemberSubscription? subscription)
        {
            if (subscription is null)
            {
                return ("No Active Plan", "bg-secondary", false);
            }

            var todayUtc = DateTime.UtcNow.Date;
            var isDateExpired = subscription.EndDateUtc.HasValue && subscription.EndDateUtc.Value.Date < todayUtc;

            if (subscription.Status == SubscriptionStatus.Active)
            {
                return isDateExpired
                    ? ("Expired", "bg-danger", false)
                    : ("Active", "bg-success", true);
            }

            if (subscription.Status == SubscriptionStatus.Paused)
            {
                if (isDateExpired)
                {
                    return ("Expired", "bg-danger", false);
                }

                return ("Paused", "bg-warning text-dark", false);
            }

            if (subscription.Status == SubscriptionStatus.Cancelled)
            {
                return ("Cancelled", "bg-secondary", false);
            }

            if (subscription.Status == SubscriptionStatus.Expired)
            {
                return ("Expired", "bg-danger", false);
            }

            return (subscription.Status.ToString(), "bg-secondary", false);
        }

        private async Task<string> EnsureMemberCheckInCodeAsync(IdentityUser user, CancellationToken cancellationToken)
        {
            var existingCode = await _db.UserClaims
                .AsNoTracking()
                .Where(claim =>
                    claim.UserId == user.Id &&
                    claim.ClaimType == MemberCheckIn.AccountCodeClaimType &&
                    claim.ClaimValue != null)
                .OrderByDescending(claim => claim.Id)
                .Select(claim => claim.ClaimValue)
                .FirstOrDefaultAsync(cancellationToken);

            if (MemberCheckIn.IsValidAccountCode(existingCode))
            {
                return existingCode!.Trim();
            }

            string generatedCode;
            var attempts = 0;
            do
            {
                generatedCode = RandomNumberGenerator.GetInt32(100000, 1000000).ToString("D6");
                attempts++;
            }
            while (attempts < 30 && await _db.UserClaims
                .AsNoTracking()
                .AnyAsync(claim =>
                    claim.ClaimType == MemberCheckIn.AccountCodeClaimType &&
                    claim.ClaimValue == generatedCode,
                    cancellationToken));

            var addClaimResult = await _userManager.AddClaimAsync(
                user,
                new Claim(MemberCheckIn.AccountCodeClaimType, generatedCode));

            if (!addClaimResult.Succeeded)
            {
                _db.UserClaims.Add(new IdentityUserClaim<string>
                {
                    UserId = user.Id,
                    ClaimType = MemberCheckIn.AccountCodeClaimType,
                    ClaimValue = generatedCode
                });

                await _db.SaveChangesAsync(cancellationToken);
            }

            return generatedCode;
        }

        private static string ToMemberActivityTitle(string? eventType)
        {
            if (string.IsNullOrWhiteSpace(eventType))
            {
                return "Activity";
            }

            return eventType.Trim().ToLowerInvariant() switch
            {
                "payment.checkout.created" => "Checkout started",
                "payment.succeeded" => "Payment successful",
                "payment.failed" => "Payment failed",
                "membership.activated" => "Membership activated",
                "staff.member.checkin" => "Checked in",
                "staff.member.checkout" => "Checked out",
                _ => eventType.Replace('.', ' ')
            };
        }

        private static decimal? CalculateBmi(decimal? heightCm, decimal? weightKg)
        {
            if (!heightCm.HasValue || !weightKg.HasValue || heightCm.Value <= 0 || weightKg.Value <= 0)
            {
                return null;
            }

            var heightInMeters = heightCm.Value / 100m;
            var bmi = weightKg.Value / (heightInMeters * heightInMeters);
            return decimal.Round(bmi, 2, MidpointRounding.AwayFromZero);
        }

        private static int CalculateCompletionPercent(MemberProfile? profile)
        {
            if (profile is null)
            {
                return 0;
            }

            var completed = 0;
            const int total = 8;

            if (!string.IsNullOrWhiteSpace(profile.FirstName)) completed++;
            if (!string.IsNullOrWhiteSpace(profile.LastName)) completed++;
            if (profile.Age.HasValue) completed++;
            if (!string.IsNullOrWhiteSpace(profile.PhoneNumber)) completed++;
            if (!string.IsNullOrWhiteSpace(profile.HomeBranchId)) completed++;
            if (profile.HeightCm.HasValue) completed++;
            if (profile.WeightKg.HasValue) completed++;
            if (!string.IsNullOrWhiteSpace(profile.ProfileImagePath)) completed++;

            return (int)Math.Round((double)completed / total * 100, MidpointRounding.AwayFromZero);
        }

        private static int CalculateCompletionPercentFromInput(MemberProfileInputModel input, string? existingImagePath)
        {
            var completed = 0;
            const int total = 8;

            if (!string.IsNullOrWhiteSpace(input.FirstName)) completed++;
            if (!string.IsNullOrWhiteSpace(input.LastName)) completed++;
            if (input.Age.HasValue) completed++;
            if (!string.IsNullOrWhiteSpace(input.PhoneNumber)) completed++;
            if (!string.IsNullOrWhiteSpace(input.HomeBranchId)) completed++;
            if (input.HeightCm.HasValue) completed++;
            if (input.WeightKg.HasValue) completed++;
            if (input.ProfileImage != null || !string.IsNullOrWhiteSpace(existingImagePath)) completed++;

            return (int)Math.Round((double)completed / total * 100, MidpointRounding.AwayFromZero);
        }

        private static string BuildDisplayName(string? firstName, string? lastName, string fallback)
        {
            var name = string.Join(' ', new[] { firstName, lastName }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));
            return string.IsNullOrWhiteSpace(name) ? fallback : name;
        }

        private static string? NormalizeBranchId(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Trim().ToUpperInvariant();
        }
    }

    public class MemberProfileInputModel
    {
        public string? Email { get; set; }

        [Required]
        [MaxLength(100)]
        [Display(Name = "First name")]
        public string? FirstName { get; set; }

        [Required]
        [MaxLength(100)]
        [Display(Name = "Last name")]
        public string? LastName { get; set; }

        [Range(10, 100)]
        public int? Age { get; set; }

        [Phone]
        [MaxLength(30)]
        [Display(Name = "Phone number")]
        public string? PhoneNumber { get; set; }

        [Required]
        [Display(Name = "Home branch")]
        public string? HomeBranchId { get; set; }

        [Range(50, 250)]
        [Display(Name = "Height (cm)")]
        public decimal? HeightCm { get; set; }

        [Range(20, 300)]
        [Display(Name = "Weight (kg)")]
        public decimal? WeightKg { get; set; }

        [Display(Name = "BMI")]
        public decimal? Bmi { get; set; }

        public int CompletionPercent { get; set; }

        [Display(Name = "Profile photo")]
        public IFormFile? ProfileImage { get; set; }

        public string? ExistingImagePath { get; set; }

        [Display(Name = "Reason for cancellation")]
        [StringLength(300)]
        public string? CancellationReason { get; set; }

        public string CurrentPlanName { get; set; } = "No plan selected";
        public string MembershipStatusLabel { get; set; } = "No Active Plan";
        public string MembershipStatusBadgeClass { get; set; } = "bg-secondary";
        public bool HasActiveMembership { get; set; }
        public bool HasOpenCancellationRequest { get; set; }
        public DateTime? OpenCancellationRequestedUtc { get; set; }
        public string? OpenCancellationReason { get; set; }
    }

    public class MemberCancellationRequestInputModel
    {
        [Display(Name = "Reason for cancellation")]
        [StringLength(300)]
        public string? CancellationReason { get; set; }
    }
}
