using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Pages.Finance
{
    [Authorize(Policy = "FinanceAccess")]
    public class FundRequestsModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public FundRequestsModel(ApplicationDbContext db)
        {
            _db = db;
        }

        public IReadOnlyList<FundRequestQueueRow> QueueRows { get; private set; } = Array.Empty<FundRequestQueueRow>();

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            var branchId = User.GetBranchId();
            var scopedInvoices = BuildBranchScopedInvoicesQuery(branchId);

            var invoices = await scopedInvoices
                .OrderByDescending(invoice => invoice.IssueDateUtc)
                .ThenByDescending(invoice => invoice.Id)
                .Take(75)
                .Select(invoice => new
                {
                    invoice.InvoiceNumber,
                    invoice.BranchId,
                    invoice.Amount,
                    invoice.Status,
                    invoice.IssueDateUtc,
                    invoice.DueDateUtc
                })
                .ToListAsync(cancellationToken);

            QueueRows = invoices
                .Select(invoice => MapToQueueRow(
                    invoice.InvoiceNumber,
                    invoice.BranchId,
                    invoice.Amount,
                    invoice.Status,
                    invoice.IssueDateUtc,
                    invoice.DueDateUtc))
                .ToList();
        }

        private IQueryable<Invoice> BuildBranchScopedInvoicesQuery(string? branchId)
        {
            var invoices = _db.Invoices.AsNoTracking();
            if (string.IsNullOrWhiteSpace(branchId))
            {
                return invoices;
            }

            return invoices.Where(invoice =>
                invoice.BranchId == branchId ||
                (invoice.BranchId == null && _db.UserClaims.Any(claim =>
                    claim.UserId == invoice.MemberUserId &&
                    claim.ClaimType == BranchAccess.BranchIdClaimType &&
                    claim.ClaimValue == branchId)));
        }

        private static FundRequestQueueRow MapToQueueRow(
            string invoiceNumber,
            string? branchId,
            decimal amount,
            InvoiceStatus status,
            DateTime issueDateUtc,
            DateTime dueDateUtc)
        {
            var stage = status switch
            {
                InvoiceStatus.Draft => "For Budget Review",
                InvoiceStatus.Unpaid => "Invoiced",
                InvoiceStatus.Overdue => "Invoiced",
                InvoiceStatus.Paid => "Paid",
                InvoiceStatus.Voided => "Voided",
                _ => "Invoiced"
            };

            var match = status switch
            {
                InvoiceStatus.Overdue => "Variance",
                InvoiceStatus.Paid => "Matched",
                InvoiceStatus.Voided => "Not Applicable",
                _ => "Pending"
            };

            var statusLabel = status switch
            {
                InvoiceStatus.Draft => "For Validation",
                InvoiceStatus.Unpaid => "For Payment",
                InvoiceStatus.Overdue => "Needs Review",
                InvoiceStatus.Paid => "Closed",
                InvoiceStatus.Voided => "Voided",
                _ => "For Validation"
            };

            return new FundRequestQueueRow(
                RequestNumber: invoiceNumber,
                Branch: string.IsNullOrWhiteSpace(branchId) ? "Unassigned" : branchId,
                DocumentReference: $"{invoiceNumber} • Due {dueDateUtc:MMM dd, yyyy}",
                CurrentStage: stage,
                ThreeWayMatch: match,
                Amount: amount,
                Owner: "Finance",
                Status: statusLabel,
                IssuedAtUtc: issueDateUtc);
        }

        public static string StageBadgeClass(string stage) => stage switch
        {
            "For Budget Review" => "badge ejc-badge",
            "Invoiced" => "badge bg-warning text-dark",
            "Paid" => "badge bg-success",
            "Voided" => "badge bg-dark",
            _ => "badge bg-secondary"
        };

        public static string MatchBadgeClass(string match) => match switch
        {
            "Matched" => "badge bg-success",
            "Variance" => "badge bg-warning text-dark",
            "Not Applicable" => "badge bg-secondary",
            _ => "badge bg-secondary"
        };

        public static string StatusBadgeClass(string status) => status switch
        {
            "For Payment" => "badge bg-info text-dark",
            "Needs Review" => "badge bg-danger",
            "Closed" => "badge bg-success",
            "Voided" => "badge bg-dark",
            _ => "badge bg-secondary"
        };

        public sealed record FundRequestQueueRow(
            string RequestNumber,
            string Branch,
            string DocumentReference,
            string CurrentStage,
            string ThreeWayMatch,
            decimal Amount,
            string Owner,
            string Status,
            DateTime IssuedAtUtc);
    }
}
