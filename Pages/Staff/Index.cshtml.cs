using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Admin;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Models.Integration;
using EJCFitnessGym.Models.Inventory;
using EJCFitnessGym.Security;
using EJCFitnessGym.Services.Staff;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Pages.Staff
{
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IStaffAttendanceService _staffAttendanceService;

        public IndexModel(ApplicationDbContext db, IStaffAttendanceService staffAttendanceService)
        {
            _db = db;
            _staffAttendanceService = staffAttendanceService;
        }

        public string ScopeLabel { get; private set; } = "All Branches";
        public int OnFloorNow { get; private set; }
        public int DueSoonInvoiceCount { get; private set; }
        public int PendingPayMongoPaymentCount { get; private set; }
        public int OpenReplacementRequestCount { get; private set; }
        public int ProductSalesTodayCount { get; private set; }
        public decimal ProductSalesTodayAmount { get; private set; }
        public IReadOnlyList<RecentAttendanceRow> RecentAttendance { get; private set; } = Array.Empty<RecentAttendanceRow>();

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            var isSuperAdmin = User.IsInRole("SuperAdmin");
            var branchId = User.GetBranchId();
            ScopeLabel = isSuperAdmin
                ? "All Branches"
                : string.IsNullOrWhiteSpace(branchId)
                    ? "No branch scope"
                    : $"Branch {branchId}";

            var todayUtc = DateTime.UtcNow.Date;
            var tomorrowUtc = todayUtc.AddDays(1);

            var dueSoonInvoices = _db.Invoices
                .AsNoTracking()
                .Where(invoice =>
                    (invoice.Status == InvoiceStatus.Unpaid || invoice.Status == InvoiceStatus.Overdue) &&
                    invoice.DueDateUtc.Date <= todayUtc.AddDays(3));

            if (!isSuperAdmin && !string.IsNullOrWhiteSpace(branchId))
            {
                dueSoonInvoices = dueSoonInvoices.Where(invoice => invoice.BranchId == branchId);
            }

            DueSoonInvoiceCount = await dueSoonInvoices.CountAsync(cancellationToken);

            var pendingPayMongoPayments = _db.Payments
                .AsNoTracking()
                .Where(payment =>
                    payment.Status == PaymentStatus.Pending &&
                    payment.Method == EJCFitnessGym.Models.Billing.PaymentMethod.OnlineGateway &&
                    payment.GatewayProvider == "PayMongo" &&
                    payment.Invoice != null);

            if (!isSuperAdmin && !string.IsNullOrWhiteSpace(branchId))
            {
                pendingPayMongoPayments = pendingPayMongoPayments.Where(payment => payment.Invoice!.BranchId == branchId);
            }

            PendingPayMongoPaymentCount = await pendingPayMongoPayments.CountAsync(cancellationToken);

            var openReplacementRequests = _db.ReplacementRequests
                .AsNoTracking()
                .Where(request =>
                    request.Status != ReplacementRequestStatus.Completed &&
                    request.Status != ReplacementRequestStatus.Rejected);

            if (!isSuperAdmin && !string.IsNullOrWhiteSpace(branchId))
            {
                openReplacementRequests = openReplacementRequests.Where(request => request.BranchId == branchId);
            }

            OpenReplacementRequestCount = await openReplacementRequests.CountAsync(cancellationToken);

            var todaySales = _db.ProductSales
                .AsNoTracking()
                .Where(sale =>
                    sale.Status == ProductSaleStatus.Completed &&
                    sale.SaleDateUtc >= todayUtc &&
                    sale.SaleDateUtc < tomorrowUtc);

            if (!isSuperAdmin && !string.IsNullOrWhiteSpace(branchId))
            {
                todaySales = todaySales.Where(sale => sale.BranchId == branchId);
            }

            ProductSalesTodayCount = await todaySales.CountAsync(cancellationToken);
            ProductSalesTodayAmount = await todaySales.SumAsync(sale => (decimal?)sale.TotalAmount, cancellationToken) ?? 0m;

            var attendanceMessages = await _db.IntegrationOutboxMessages
                .AsNoTracking()
                .Where(message =>
                    message.Target == IntegrationOutboxTarget.BackOffice &&
                    (message.EventType == StaffAttendanceEvents.CheckInEventType ||
                     message.EventType == StaffAttendanceEvents.CheckOutEventType))
                .OrderByDescending(message => message.CreatedUtc)
                .ThenByDescending(message => message.Id)
                .Take(1000)
                .ToListAsync(cancellationToken);

            var attendanceEvents = attendanceMessages
                .Select(StaffAttendanceEvents.TryParse)
                .Where(evt => evt is not null && IsInCurrentBranchScope(evt.BranchId, isSuperAdmin, branchId))
                .Cast<StaffAttendanceEvent>()
                .ToList();

            var latestByMember = attendanceEvents
                .GroupBy(evt => evt.MemberUserId, StringComparer.Ordinal)
                .Select(group => group
                    .OrderByDescending(evt => evt.EventUtc)
                    .ThenByDescending(evt => evt.EventId)
                    .First())
                .ToList();

            OnFloorNow = latestByMember.Count(evt =>
                StaffAttendanceEvents.IsCheckIn(evt.EventType) &&
                !_staffAttendanceService.IsSessionTimedOut(evt.EventUtc));

            RecentAttendance = attendanceEvents
                .OrderByDescending(evt => evt.EventUtc)
                .ThenByDescending(evt => evt.EventId)
                .Take(8)
                .Select(evt => new RecentAttendanceRow
                {
                    TimeLocal = evt.EventUtc.ToLocalTime(),
                    MemberDisplayName = evt.MemberDisplayName,
                    BranchId = evt.BranchId,
                    EventLabel = StaffAttendanceEvents.ActionLabel(evt.EventType, evt.IsAutoCheckout),
                    EventBadgeClass = StaffAttendanceEvents.IsCheckIn(evt.EventType)
                        ? "badge bg-info text-dark"
                        : evt.IsAutoCheckout
                            ? "badge bg-warning text-dark"
                            : "badge ejc-badge"
                })
                .ToList();
        }

        private static bool IsInCurrentBranchScope(string? eventBranchId, bool isSuperAdmin, string? branchId)
        {
            if (isSuperAdmin)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(branchId) &&
                !string.IsNullOrWhiteSpace(eventBranchId) &&
                string.Equals(eventBranchId, branchId, StringComparison.OrdinalIgnoreCase);
        }

        public sealed class RecentAttendanceRow
        {
            public DateTime TimeLocal { get; init; }
            public string MemberDisplayName { get; init; } = string.Empty;
            public string? BranchId { get; init; }
            public string EventLabel { get; init; } = string.Empty;
            public string EventBadgeClass { get; init; } = "badge bg-secondary";
        }
    }
}
