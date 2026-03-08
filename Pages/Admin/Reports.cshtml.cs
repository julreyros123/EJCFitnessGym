using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Admin;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Models.Inventory;
using EJCFitnessGym.Security;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Pages.Admin
{
    public class ReportsModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public ReportsModel(ApplicationDbContext db)
        {
            _db = db;
        }

        public DateTime GeneratedAtLocal { get; private set; } = DateTime.Now;
        public string ScopeLabel { get; private set; } = "All Branches";
        public decimal RevenueLast30Days { get; private set; }
        public int SuccessfulPaymentsLast30Days { get; private set; }
        public int FailedPaymentsLast30Days { get; private set; }
        public int PendingInvoices { get; private set; }
        public int OverdueInvoices { get; private set; }
        public int LowStockProducts { get; private set; }
        public int OpenSupplyRequests { get; private set; }
        public int OpenReplacementRequests { get; private set; }

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            var branchId = User.IsInRole("SuperAdmin") ? null : User.GetBranchId();
            ScopeLabel = string.IsNullOrWhiteSpace(branchId)
                ? "All Branches"
                : $"Branch {branchId}";

            var periodStartUtc = DateTime.UtcNow.Date.AddDays(-30);

            var payments = _db.Payments.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(branchId))
            {
                payments = payments.Where(p => p.BranchId == branchId);
            }

            RevenueLast30Days = await payments
                .Where(p => p.Status == PaymentStatus.Succeeded && p.PaidAtUtc >= periodStartUtc)
                .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;

            SuccessfulPaymentsLast30Days = await payments.CountAsync(
                p => p.Status == PaymentStatus.Succeeded && p.PaidAtUtc >= periodStartUtc,
                cancellationToken);

            FailedPaymentsLast30Days = await payments.CountAsync(
                p => p.Status == PaymentStatus.Failed && p.PaidAtUtc >= periodStartUtc,
                cancellationToken);

            var invoices = _db.Invoices.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(branchId))
            {
                invoices = invoices.Where(i => i.BranchId == branchId);
            }

            PendingInvoices = await invoices.CountAsync(
                i => i.Status == InvoiceStatus.Unpaid,
                cancellationToken);
            OverdueInvoices = await invoices.CountAsync(
                i => i.Status == InvoiceStatus.Overdue,
                cancellationToken);

            var products = _db.RetailProducts
                .AsNoTracking()
                .Where(p => p.IsActive);
            if (!string.IsNullOrWhiteSpace(branchId))
            {
                products = products.Where(p => p.BranchId == branchId);
            }

            LowStockProducts = await products.CountAsync(
                p => p.StockQuantity <= p.ReorderLevel,
                cancellationToken);

            var supplyRequests = _db.SupplyRequests.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(branchId))
            {
                supplyRequests = supplyRequests.Where(r => r.BranchId == branchId);
            }

            OpenSupplyRequests = await supplyRequests.CountAsync(
                r => r.Stage != SupplyRequestStage.Paid &&
                     r.Stage != SupplyRequestStage.Audited &&
                     r.Stage != SupplyRequestStage.Cancelled,
                cancellationToken);

            var replacementRequests = _db.ReplacementRequests.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(branchId))
            {
                replacementRequests = replacementRequests.Where(r => r.BranchId == branchId);
            }

            OpenReplacementRequests = await replacementRequests.CountAsync(
                r => r.Status == ReplacementRequestStatus.Requested ||
                     r.Status == ReplacementRequestStatus.InReview,
                cancellationToken);

            GeneratedAtLocal = DateTime.Now;
        }
    }
}
