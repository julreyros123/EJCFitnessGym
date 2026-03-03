using EJCFitnessGym.Data;
using EJCFitnessGym.Models;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Security;
using EJCFitnessGym.Services.Payments;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Pages.Staff
{
    public class PlanStatusModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IPayMongoMembershipReconciliationService? _payMongoMembershipReconciliationService;
        private readonly ILogger<PlanStatusModel>? _logger;

        public PlanStatusModel(
            ApplicationDbContext db,
            UserManager<IdentityUser> userManager,
            IPayMongoMembershipReconciliationService? payMongoMembershipReconciliationService = null,
            ILogger<PlanStatusModel>? logger = null)
        {
            _db = db;
            _userManager = userManager;
            _payMongoMembershipReconciliationService = payMongoMembershipReconciliationService;
            _logger = logger;
        }

        public IReadOnlyList<PlanStatusRow> Members { get; private set; } = Array.Empty<PlanStatusRow>();

        public int ActiveCount { get; private set; }
        public int ExpiringSoonCount { get; private set; }
        public int PendingPaymentCount { get; private set; }
        public int ExpiredCount { get; private set; }

        public async Task OnGet(CancellationToken cancellationToken)
        {
            var isSuperAdmin = User.IsInRole("SuperAdmin");
            var branchId = User.GetBranchId();
            if (!isSuperAdmin && string.IsNullOrWhiteSpace(branchId))
            {
                Members = Array.Empty<PlanStatusRow>();
                return;
            }

            var latestSubscriptions = await _db.MemberSubscriptions
                .AsNoTracking()
                .Include(subscription => subscription.SubscriptionPlan)
                .GroupBy(subscription => subscription.MemberUserId)
                .Select(group => group
                    .OrderByDescending(subscription => subscription.StartDateUtc)
                    .ThenByDescending(subscription => subscription.Id)
                    .First())
                .ToListAsync(cancellationToken);

            if (latestSubscriptions.Count == 0)
            {
                Members = Array.Empty<PlanStatusRow>();
                return;
            }

            var latestByMember = latestSubscriptions
                .ToDictionary(subscription => subscription.MemberUserId, StringComparer.Ordinal);

            var allMemberIds = latestByMember.Keys.ToList();
            var branchByMemberId = await _db.UserClaims
                .AsNoTracking()
                .Where(claim =>
                    claim.ClaimType == BranchAccess.BranchIdClaimType &&
                    claim.ClaimValue != null &&
                    allMemberIds.Contains(claim.UserId))
                .GroupBy(claim => claim.UserId)
                .Select(group => new
                {
                    MemberUserId = group.Key,
                    BranchId = group
                        .OrderByDescending(claim => claim.Id)
                        .Select(claim => claim.ClaimValue)
                        .FirstOrDefault()
                })
                .ToDictionaryAsync(
                    item => item.MemberUserId,
                    item => item.BranchId,
                    StringComparer.Ordinal,
                    cancellationToken);

            var scopedMemberIds = isSuperAdmin
                ? allMemberIds
                : allMemberIds
                    .Where(memberUserId =>
                        branchByMemberId.TryGetValue(memberUserId, out var memberBranchId) &&
                        string.Equals(memberBranchId, branchId, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (scopedMemberIds.Count == 0)
            {
                Members = Array.Empty<PlanStatusRow>();
                return;
            }

            if (_payMongoMembershipReconciliationService is not null)
            {
                var membersWithPendingPayMongo = await _db.Payments
                    .AsNoTracking()
                    .Where(payment =>
                        payment.Status == PaymentStatus.Pending &&
                        payment.Method == PaymentMethod.OnlineGateway &&
                        payment.GatewayProvider == "PayMongo" &&
                        payment.Invoice != null &&
                        scopedMemberIds.Contains(payment.Invoice.MemberUserId))
                    .Select(payment => payment.Invoice!.MemberUserId)
                    .Where(memberUserId => !string.IsNullOrWhiteSpace(memberUserId))
                    .Distinct()
                    .ToListAsync(cancellationToken);

                foreach (var memberUserId in membersWithPendingPayMongo)
                {
                    try
                    {
                        await _payMongoMembershipReconciliationService
                            .ReconcilePendingMemberPaymentsAsync(memberUserId, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(
                            ex,
                            "PayMongo payment reconciliation failed while building staff plan status for member {MemberUserId}.",
                            memberUserId);
                    }
                }
            }

            var usersById = await _userManager.Users
                .AsNoTracking()
                .Where(user => scopedMemberIds.Contains(user.Id))
                .Select(user => new
                {
                    user.Id,
                    Display = user.Email ?? user.UserName ?? user.Id
                })
                .ToDictionaryAsync(item => item.Id, item => item.Display, StringComparer.Ordinal, cancellationToken);

            var profilesByUserId = await _db.MemberProfiles
                .AsNoTracking()
                .Where(profile => scopedMemberIds.Contains(profile.UserId))
                .ToDictionaryAsync(profile => profile.UserId, profile => profile, StringComparer.Ordinal, cancellationToken);

            var openInvoices = await _db.Invoices
                .AsNoTracking()
                .Where(invoice =>
                    scopedMemberIds.Contains(invoice.MemberUserId) &&
                    (invoice.Status == InvoiceStatus.Unpaid || invoice.Status == InvoiceStatus.Overdue))
                .Select(invoice => new
                {
                    invoice.MemberUserId,
                    invoice.InvoiceNumber,
                    invoice.DueDateUtc,
                    invoice.Amount,
                    invoice.Status
                })
                .ToListAsync(cancellationToken);

            var nextInvoiceByMemberId = openInvoices
                .GroupBy(invoice => invoice.MemberUserId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderBy(invoice => invoice.DueDateUtc)
                        .ThenBy(invoice => invoice.InvoiceNumber, StringComparer.OrdinalIgnoreCase)
                        .First(),
                    StringComparer.Ordinal);

            var outstandingBalanceByMemberId = openInvoices
                .GroupBy(invoice => invoice.MemberUserId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Sum(invoice => invoice.Amount),
                    StringComparer.Ordinal);

            var pendingPaymentByMemberId = await _db.Payments
                .AsNoTracking()
                .Where(payment =>
                    payment.Status == PaymentStatus.Pending &&
                    payment.Method == PaymentMethod.OnlineGateway &&
                    payment.GatewayProvider == "PayMongo" &&
                    payment.Invoice != null &&
                    scopedMemberIds.Contains(payment.Invoice.MemberUserId))
                .Select(payment => payment.Invoice!.MemberUserId)
                .Distinct()
                .ToListAsync(cancellationToken);

            var pendingSet = pendingPaymentByMemberId
                .Where(memberUserId => !string.IsNullOrWhiteSpace(memberUserId))
                .ToHashSet(StringComparer.Ordinal);

            var todayUtc = DateTime.UtcNow.Date;
            var rows = new List<PlanStatusRow>(scopedMemberIds.Count);

            foreach (var memberUserId in scopedMemberIds)
            {
                if (!latestByMember.TryGetValue(memberUserId, out var subscription))
                {
                    continue;
                }

                var profile = profilesByUserId.TryGetValue(memberUserId, out var memberProfile) ? memberProfile : null;
                var memberDisplayName = BuildMemberDisplayName(
                    profile,
                    usersById.TryGetValue(memberUserId, out var fallbackDisplay) ? fallbackDisplay : memberUserId);

                var hasPendingPayment = pendingSet.Contains(memberUserId);
                nextInvoiceByMemberId.TryGetValue(memberUserId, out var nextInvoice);
                outstandingBalanceByMemberId.TryGetValue(memberUserId, out var outstandingBalance);

                var status = ResolveStatus(subscription, nextInvoice?.Status, hasPendingPayment, todayUtc);

                rows.Add(new PlanStatusRow
                {
                    MemberUserId = memberUserId,
                    MemberName = memberDisplayName,
                    PlanName = subscription.SubscriptionPlan?.Name ?? "No Plan",
                    RenewalDateUtc = subscription.EndDateUtc,
                    NextBillingDateUtc = nextInvoice?.DueDateUtc,
                    ReminderDateUtc = nextInvoice?.DueDateUtc.AddDays(-3),
                    OutstandingBalance = outstandingBalance,
                    HasPendingPayment = hasPendingPayment,
                    PendingInvoiceNumber = hasPendingPayment ? nextInvoice?.InvoiceNumber : null,
                    StatusLabel = status.Label,
                    StatusBadgeClass = status.BadgeClass
                });
            }

            Members = rows
                .OrderBy(row => row.StatusSortOrder)
                .ThenBy(row => row.NextBillingDateUtc ?? DateTime.MaxValue)
                .ThenBy(row => row.MemberName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ActiveCount = Members.Count(row => string.Equals(row.StatusLabel, "Active", StringComparison.OrdinalIgnoreCase));
            ExpiringSoonCount = Members.Count(row => string.Equals(row.StatusLabel, "Expiring Soon", StringComparison.OrdinalIgnoreCase));
            PendingPaymentCount = Members.Count(row => row.HasPendingPayment);
            ExpiredCount = Members.Count(row => string.Equals(row.StatusLabel, "Expired", StringComparison.OrdinalIgnoreCase));
        }

        private static (string Label, string BadgeClass) ResolveStatus(
            MemberSubscription subscription,
            InvoiceStatus? nextInvoiceStatus,
            bool hasPendingPayment,
            DateTime todayUtc)
        {
            if (hasPendingPayment)
            {
                return ("Payment Pending", "bg-info text-dark");
            }

            if (nextInvoiceStatus == InvoiceStatus.Overdue)
            {
                return ("Expired", "bg-danger");
            }

            if (subscription.Status == SubscriptionStatus.Active)
            {
                if (subscription.EndDateUtc.HasValue)
                {
                    var endDateUtc = subscription.EndDateUtc.Value.Date;
                    if (endDateUtc < todayUtc)
                    {
                        return ("Expired", "bg-danger");
                    }

                    if (endDateUtc <= todayUtc.AddDays(3))
                    {
                        return ("Expiring Soon", "bg-warning text-dark");
                    }
                }

                return ("Active", "ejc-badge");
            }

            if (subscription.Status == SubscriptionStatus.Paused)
            {
                return ("Paused", "bg-secondary");
            }

            if (subscription.Status == SubscriptionStatus.Cancelled)
            {
                return ("Cancelled", "bg-secondary");
            }

            if (subscription.Status == SubscriptionStatus.Expired)
            {
                return ("Expired", "bg-danger");
            }

            return (subscription.Status.ToString(), "bg-secondary");
        }

        private static string BuildMemberDisplayName(MemberProfile? profile, string fallback)
        {
            var firstName = profile?.FirstName?.Trim();
            var lastName = profile?.LastName?.Trim();

            if (!string.IsNullOrWhiteSpace(firstName) || !string.IsNullOrWhiteSpace(lastName))
            {
                return string.Join(' ', new[] { firstName, lastName }.Where(value => !string.IsNullOrWhiteSpace(value)));
            }

            return fallback;
        }

        public sealed class PlanStatusRow
        {
            public string MemberUserId { get; init; } = string.Empty;
            public string MemberName { get; init; } = string.Empty;
            public string PlanName { get; init; } = string.Empty;
            public DateTime? RenewalDateUtc { get; init; }
            public DateTime? NextBillingDateUtc { get; init; }
            public DateTime? ReminderDateUtc { get; init; }
            public decimal OutstandingBalance { get; init; }
            public bool HasPendingPayment { get; init; }
            public string? PendingInvoiceNumber { get; init; }
            public string StatusLabel { get; init; } = "Unknown";
            public string StatusBadgeClass { get; init; } = "bg-secondary";

            public int StatusSortOrder =>
                StatusLabel switch
                {
                    "Expired" => 0,
                    "Payment Pending" => 1,
                    "Expiring Soon" => 2,
                    "Active" => 3,
                    "Paused" => 4,
                    "Cancelled" => 5,
                    _ => 9
                };
        }
    }
}
