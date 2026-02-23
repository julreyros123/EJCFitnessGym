using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Pages.Finance
{
    [Authorize(Policy = "FinanceAccess")]
    public class WeeklySalesAuditModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public WeeklySalesAuditModel(ApplicationDbContext db)
        {
            _db = db;
        }

        public DateTime AsOfUtc { get; private set; }

        public IReadOnlyList<WeeklySalesAuditRow> WeeklyRows { get; private set; } = Array.Empty<WeeklySalesAuditRow>();

        public decimal LastFourWeeksSales => WeeklyRows.Take(4).Sum(row => row.TotalSales);

        public int LastFourWeeksTransactions => WeeklyRows.Take(4).Sum(row => row.TransactionCount);

        public async Task OnGetAsync()
        {
            AsOfUtc = DateTime.UtcNow;
            var branchId = User.GetBranchId();
            var asOfDateUtc = AsOfUtc.Date;
            var currentWeekStart = StartOfWeek(asOfDateUtc, DayOfWeek.Monday);
            var earliestWeekStart = currentWeekStart.AddDays(-7 * 7);

            var scopedInvoiceIds = BuildBranchScopedInvoiceIdsQuery(branchId);
            var paymentRows = await _db.Payments
                .AsNoTracking()
                .Where(payment =>
                    payment.Status == PaymentStatus.Succeeded &&
                    scopedInvoiceIds.Contains(payment.InvoiceId) &&
                    payment.PaidAtUtc >= earliestWeekStart)
                .Select(payment => new PaymentAuditEntry
                {
                    PaidAtUtc = payment.PaidAtUtc,
                    Amount = payment.Amount,
                    Method = payment.Method,
                    GatewayProvider = payment.GatewayProvider
                })
                .ToListAsync();

            var grouped = paymentRows
                .GroupBy(entry => StartOfWeek(entry.PaidAtUtc.Date, DayOfWeek.Monday))
                .ToDictionary(group => group.Key, group => group.ToList());

            var weeks = new List<WeeklySalesAuditRow>();
            for (var i = 0; i < 8; i++)
            {
                var weekStart = currentWeekStart.AddDays(-7 * i);
                var weekEnd = weekStart.AddDays(6);
                grouped.TryGetValue(weekStart, out var entriesForWeek);
                entriesForWeek ??= [];

                decimal totalSales = 0m;
                decimal staffCollectedSales = 0m;
                decimal gatewaySales = 0m;
                var transactionCount = 0;

                foreach (var entry in entriesForWeek)
                {
                    transactionCount++;
                    totalSales += entry.Amount;

                    var isGateway = entry.Method == PaymentMethod.OnlineGateway ||
                        (!string.IsNullOrWhiteSpace(entry.GatewayProvider) &&
                         entry.GatewayProvider.Contains("PayMongo", StringComparison.OrdinalIgnoreCase));

                    if (isGateway)
                    {
                        gatewaySales += entry.Amount;
                    }
                    else
                    {
                        staffCollectedSales += entry.Amount;
                    }
                }

                var auditStatus = weekEnd < asOfDateUtc
                    ? "Ready for Audit"
                    : "Current Week";

                weeks.Add(new WeeklySalesAuditRow(
                    weekStart,
                    weekEnd,
                    transactionCount,
                    totalSales,
                    staffCollectedSales,
                    gatewaySales,
                    auditStatus));
            }

            WeeklyRows = weeks;
        }

        private IQueryable<int> BuildBranchScopedInvoiceIdsQuery(string? branchId)
        {
            var invoiceIds = _db.Invoices.AsNoTracking();

            if (string.IsNullOrWhiteSpace(branchId))
            {
                return invoiceIds.Select(invoice => invoice.Id);
            }

            return invoiceIds
                .Where(invoice =>
                    invoice.BranchId == branchId ||
                    (invoice.BranchId == null && _db.UserClaims.Any(claim =>
                        claim.UserId == invoice.MemberUserId &&
                        claim.ClaimType == BranchAccess.BranchIdClaimType &&
                        claim.ClaimValue == branchId)))
                .Select(invoice => invoice.Id);
        }

        private static DateTime StartOfWeek(DateTime value, DayOfWeek firstDayOfWeek)
        {
            var date = value.Date;
            var diff = (7 + (date.DayOfWeek - firstDayOfWeek)) % 7;
            return date.AddDays(-diff);
        }
    }

    public sealed record WeeklySalesAuditRow(
        DateTime WeekStartUtc,
        DateTime WeekEndUtc,
        int TransactionCount,
        decimal TotalSales,
        decimal StaffCollectedSales,
        decimal GatewaySales,
        string AuditStatus);

    public sealed class PaymentAuditEntry
    {
        public DateTime PaidAtUtc { get; init; }

        public decimal Amount { get; init; }

        public PaymentMethod Method { get; init; }

        public string? GatewayProvider { get; init; }
    }
}
