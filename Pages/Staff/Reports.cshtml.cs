using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Admin;
using EJCFitnessGym.Models.Integration;
using EJCFitnessGym.Models.Inventory;
using EJCFitnessGym.Security;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Pages.Staff
{
    public class ReportsModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public ReportsModel(ApplicationDbContext db)
        {
            _db = db;
        }

        public string ScopeLabel { get; private set; } = "All Branches";
        public DateTime PeriodStartUtc { get; private set; } = DateTime.UtcNow.Date.AddDays(-6);
        public int CheckInCount { get; private set; }
        public int CheckOutCount { get; private set; }
        public int AutoCheckOutCount { get; private set; }
        public int ProductSalesCount { get; private set; }
        public decimal ProductSalesAmount { get; private set; }
        public int PendingReplacementCount { get; private set; }
        public int ExpiringPlanCount { get; private set; }
        public IReadOnlyList<DailyStaffReportRow> DailyRows { get; private set; } = Array.Empty<DailyStaffReportRow>();

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            var branchId = User.IsInRole("SuperAdmin") ? null : User.GetBranchId();
            ScopeLabel = string.IsNullOrWhiteSpace(branchId)
                ? "All Branches"
                : $"Branch {branchId}";

            PeriodStartUtc = DateTime.UtcNow.Date.AddDays(-6);
            var todayUtc = DateTime.UtcNow.Date;

            var attendanceMessages = await _db.IntegrationOutboxMessages
                .AsNoTracking()
                .Where(message =>
                    message.Target == IntegrationOutboxTarget.BackOffice &&
                    (message.EventType == StaffAttendanceEvents.CheckInEventType ||
                     message.EventType == StaffAttendanceEvents.CheckOutEventType) &&
                    message.CreatedUtc >= PeriodStartUtc)
                .OrderByDescending(message => message.CreatedUtc)
                .ThenByDescending(message => message.Id)
                .Take(1200)
                .ToListAsync(cancellationToken);

            var attendanceEvents = attendanceMessages
                .Select(StaffAttendanceEvents.TryParse)
                .Where(evt => evt is not null)
                .Cast<StaffAttendanceEvent>()
                .Where(evt =>
                    string.IsNullOrWhiteSpace(branchId) ||
                    string.Equals(evt.BranchId, branchId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            CheckInCount = attendanceEvents.Count(evt => StaffAttendanceEvents.IsCheckIn(evt.EventType));
            CheckOutCount = attendanceEvents.Count(evt => StaffAttendanceEvents.IsCheckOut(evt.EventType));
            AutoCheckOutCount = attendanceEvents.Count(evt => StaffAttendanceEvents.IsCheckOut(evt.EventType) && evt.IsAutoCheckout);

            var salesQuery = _db.ProductSales
                .AsNoTracking()
                .Where(sale =>
                    sale.Status == ProductSaleStatus.Completed &&
                    sale.SaleDateUtc >= PeriodStartUtc);

            if (!string.IsNullOrWhiteSpace(branchId))
            {
                salesQuery = salesQuery.Where(sale => sale.BranchId == branchId);
            }

            ProductSalesCount = await salesQuery.CountAsync(cancellationToken);
            ProductSalesAmount = await salesQuery
                .SumAsync(sale => (decimal?)sale.TotalAmount, cancellationToken) ?? 0m;

            var pendingReplacements = _db.ReplacementRequests
                .AsNoTracking()
                .Where(request =>
                    request.Status == ReplacementRequestStatus.Requested ||
                    request.Status == ReplacementRequestStatus.InReview);

            if (!string.IsNullOrWhiteSpace(branchId))
            {
                pendingReplacements = pendingReplacements.Where(request => request.BranchId == branchId);
            }

            PendingReplacementCount = await pendingReplacements.CountAsync(cancellationToken);

            ExpiringPlanCount = await _db.MemberSubscriptions
                .AsNoTracking()
                .CountAsync(subscription =>
                    subscription.Status == Models.Billing.SubscriptionStatus.Active &&
                    subscription.EndDateUtc.HasValue &&
                    subscription.EndDateUtc.Value.Date >= todayUtc &&
                    subscription.EndDateUtc.Value.Date <= todayUtc.AddDays(7),
                    cancellationToken);

            var salesByDay = await salesQuery
                .GroupBy(sale => sale.SaleDateUtc.Date)
                .Select(group => new
                {
                    DateUtc = group.Key,
                    Amount = group.Sum(item => item.TotalAmount)
                })
                .ToListAsync(cancellationToken);

            var salesMap = salesByDay.ToDictionary(item => item.DateUtc, item => item.Amount);
            var checkInMap = attendanceEvents
                .Where(evt => StaffAttendanceEvents.IsCheckIn(evt.EventType))
                .GroupBy(evt => evt.EventUtc.Date)
                .ToDictionary(group => group.Key, group => group.Count());
            var checkOutMap = attendanceEvents
                .Where(evt => StaffAttendanceEvents.IsCheckOut(evt.EventType))
                .GroupBy(evt => evt.EventUtc.Date)
                .ToDictionary(group => group.Key, group => group.Count());

            var rows = new List<DailyStaffReportRow>(7);
            for (var dayOffset = 0; dayOffset < 7; dayOffset++)
            {
                var day = PeriodStartUtc.AddDays(dayOffset);
                rows.Add(new DailyStaffReportRow
                {
                    DateUtc = day,
                    CheckIns = checkInMap.TryGetValue(day, out var dayCheckIns) ? dayCheckIns : 0,
                    CheckOuts = checkOutMap.TryGetValue(day, out var dayCheckOuts) ? dayCheckOuts : 0,
                    SalesAmount = salesMap.TryGetValue(day, out var daySales) ? daySales : 0m
                });
            }

            DailyRows = rows;
        }

        public sealed class DailyStaffReportRow
        {
            public DateTime DateUtc { get; init; }
            public int CheckIns { get; init; }
            public int CheckOuts { get; init; }
            public decimal SalesAmount { get; init; }
        }
    }
}
