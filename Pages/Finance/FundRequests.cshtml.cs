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
        public int AwaitingClearanceCount { get; private set; }
        public int ReadyToReleaseCount { get; private set; }
        public int ReleasedCount { get; private set; }
        public int ReturnedCount { get; private set; }

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

            AwaitingClearanceCount = QueueRows.Count(row =>
                string.Equals(row.ClearanceState, "Waiting Clearance", StringComparison.OrdinalIgnoreCase));
            ReadyToReleaseCount = QueueRows.Count(row =>
                string.Equals(row.FinanceAction, "Ready to Release", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(row.FinanceAction, "Priority Release", StringComparison.OrdinalIgnoreCase));
            ReleasedCount = QueueRows.Count(row =>
                string.Equals(row.FinanceAction, "Released", StringComparison.OrdinalIgnoreCase));
            ReturnedCount = QueueRows.Count(row =>
                string.Equals(row.QueueStatus, "Returned", StringComparison.OrdinalIgnoreCase));
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
            var clearanceState = status switch
            {
                InvoiceStatus.Draft => "Waiting Clearance",
                InvoiceStatus.Voided => "Cancelled",
                _ => "Cleared"
            };

            var financeAction = status switch
            {
                InvoiceStatus.Draft => "On Hold",
                InvoiceStatus.Unpaid => "Ready to Release",
                InvoiceStatus.Overdue => "Priority Release",
                InvoiceStatus.Paid => "Released",
                InvoiceStatus.Voided => "No Release",
                _ => "On Hold"
            };

            var queueStatus = status switch
            {
                InvoiceStatus.Draft => "Pending Clearance",
                InvoiceStatus.Unpaid => "Pending Release",
                InvoiceStatus.Overdue => "Urgent",
                InvoiceStatus.Paid => "Completed",
                InvoiceStatus.Voided => "Returned",
                _ => "Pending Clearance"
            };

            return new FundRequestQueueRow(
                RequestNumber: invoiceNumber,
                Branch: string.IsNullOrWhiteSpace(branchId) ? "Unassigned" : branchId,
                RequestedAtUtc: issueDateUtc,
                Amount: amount,
                ClearanceState: clearanceState,
                FinanceAction: financeAction,
                QueueStatus: queueStatus,
                DueDateUtc: dueDateUtc);
        }

        public static string ClearanceBadgeClass(string clearanceState) => clearanceState switch
        {
            "Cleared" => "badge bg-success",
            "Waiting Clearance" => "badge bg-secondary",
            "Cancelled" => "badge bg-danger",
            _ => "badge bg-secondary"
        };

        public static string FinanceActionBadgeClass(string financeAction) => financeAction switch
        {
            "Ready to Release" => "badge bg-info text-dark",
            "Priority Release" => "badge bg-warning text-dark",
            "Released" => "badge bg-success",
            "No Release" => "badge bg-danger",
            _ => "badge bg-secondary"
        };

        public static string QueueStatusBadgeClass(string queueStatus) => queueStatus switch
        {
            "Pending Release" => "badge bg-info text-dark",
            "Urgent" => "badge bg-warning text-dark",
            "Completed" => "badge bg-success",
            "Returned" => "badge bg-danger",
            _ => "badge bg-secondary"
        };

        public sealed record FundRequestQueueRow(
            string RequestNumber,
            string Branch,
            DateTime RequestedAtUtc,
            decimal Amount,
            string ClearanceState,
            string FinanceAction,
            string QueueStatus,
            DateTime DueDateUtc);
    }
}
