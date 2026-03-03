using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Finance;
using EJCFitnessGym.Models.Inventory;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Services.Inventory
{
    public class SupplyRequestService : ISupplyRequestService
    {
        private readonly ApplicationDbContext _db;

        public SupplyRequestService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<SupplyRequest> CreateRequestAsync(SupplyRequest request, CancellationToken cancellationToken = default)
        {
            request.RequestNumber = GenerateRequestNumber();
            request.Stage = SupplyRequestStage.Requested;
            request.CreatedAtUtc = DateTime.UtcNow;

            _db.SupplyRequests.Add(request);
            await _db.SaveChangesAsync(cancellationToken);
            return request;
        }

        public async Task<SupplyRequest?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return await _db.SupplyRequests
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        }

        public async Task<SupplyRequest?> GetByRequestNumberAsync(string requestNumber, CancellationToken cancellationToken = default)
        {
            return await _db.SupplyRequests
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.RequestNumber == requestNumber, cancellationToken);
        }

        public async Task<IReadOnlyList<SupplyRequest>> GetRequestsAsync(
            string? branchId,
            SupplyRequestStage? stage = null,
            int take = 100,
            CancellationToken cancellationToken = default)
        {
            var query = _db.SupplyRequests.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(branchId))
            {
                query = query.Where(r => r.BranchId == branchId);
            }

            if (stage.HasValue)
            {
                query = query.Where(r => r.Stage == stage.Value);
            }

            return await query
                .OrderByDescending(r => r.CreatedAtUtc)
                .Take(take)
                .ToListAsync(cancellationToken);
        }

        public async Task<SupplyRequest> ApproveAsync(int requestId, string approvedByUserId, CancellationToken cancellationToken = default)
        {
            var request = await _db.SupplyRequests.FindAsync(new object[] { requestId }, cancellationToken)
                ?? throw new InvalidOperationException($"Supply request {requestId} not found.");

            if (request.Stage != SupplyRequestStage.Requested)
            {
                throw new InvalidOperationException($"Request must be in Requested stage to approve. Current: {request.Stage}");
            }

            request.Stage = SupplyRequestStage.Approved;
            request.ApprovedByUserId = approvedByUserId;
            request.ApprovedAtUtc = DateTime.UtcNow;
            request.UpdatedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);
            return request;
        }

        public async Task<SupplyRequest> MarkOrderedAsync(int requestId, CancellationToken cancellationToken = default)
        {
            var request = await _db.SupplyRequests.FindAsync(new object[] { requestId }, cancellationToken)
                ?? throw new InvalidOperationException($"Supply request {requestId} not found.");

            if (request.Stage != SupplyRequestStage.Approved)
            {
                throw new InvalidOperationException($"Request must be in Approved stage to mark as ordered. Current: {request.Stage}");
            }

            request.Stage = SupplyRequestStage.Ordered;
            request.OrderedAtUtc = DateTime.UtcNow;
            request.UpdatedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);
            return request;
        }

        public async Task<SupplyRequest> ReceiveDraftAsync(
            int requestId,
            int receivedQuantity,
            decimal actualUnitCost,
            string receivedByUserId,
            CancellationToken cancellationToken = default)
        {
            var request = await _db.SupplyRequests.FindAsync(new object[] { requestId }, cancellationToken)
                ?? throw new InvalidOperationException($"Supply request {requestId} not found.");

            if (request.Stage != SupplyRequestStage.Ordered)
            {
                throw new InvalidOperationException($"Request must be in Ordered stage to receive. Current: {request.Stage}");
            }

            request.Stage = SupplyRequestStage.ReceivedDraft;
            request.ReceivedQuantity = receivedQuantity;
            request.ActualUnitCost = actualUnitCost;
            request.ReceivedByUserId = receivedByUserId;
            request.ReceivedAtUtc = DateTime.UtcNow;
            request.UpdatedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);
            return request;
        }

        public async Task<SupplyRequest> ConfirmReceiptAsync(int requestId, CancellationToken cancellationToken = default)
        {
            var request = await _db.SupplyRequests.FindAsync(new object[] { requestId }, cancellationToken)
                ?? throw new InvalidOperationException($"Supply request {requestId} not found.");

            if (request.Stage != SupplyRequestStage.ReceivedDraft)
            {
                throw new InvalidOperationException($"Request must be in ReceivedDraft stage to confirm. Current: {request.Stage}");
            }

            request.Stage = SupplyRequestStage.ReceivedConfirmed;
            request.UpdatedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);
            return request;
        }

        public async Task<(SupplyRequest Request, FinanceExpenseRecord Expense)> CreateExpenseAsync(
            int requestId,
            string? createdByUserId,
            CancellationToken cancellationToken = default)
        {
            var request = await _db.SupplyRequests.FindAsync(new object[] { requestId }, cancellationToken)
                ?? throw new InvalidOperationException($"Supply request {requestId} not found.");

            if (request.Stage != SupplyRequestStage.ReceivedConfirmed)
            {
                throw new InvalidOperationException($"Request must be in ReceivedConfirmed stage to create expense. Current: {request.Stage}");
            }

            var quantity = request.ReceivedQuantity ?? request.RequestedQuantity;
            var unitCost = request.ActualUnitCost ?? request.EstimatedUnitCost ?? 0m;
            var totalAmount = quantity * unitCost;

            var expense = new FinanceExpenseRecord
            {
                BranchId = request.BranchId,
                Category = "Inventory",
                Name = $"[{request.RequestNumber}] {request.ItemName} x{quantity}",
                Amount = totalAmount,
                ExpenseDateUtc = DateTime.UtcNow,
                IsRecurring = false,
                IsActive = true,
                Notes = $"Supply request auto-expense. Unit cost: {unitCost:N2}"
            };

            _db.FinanceExpenseRecords.Add(expense);
            await _db.SaveChangesAsync(cancellationToken);

            request.Stage = SupplyRequestStage.Invoiced;
            request.LinkedExpenseId = expense.Id;
            request.InvoicedAtUtc = DateTime.UtcNow;
            request.UpdatedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);

            return (request, expense);
        }

        public async Task<SupplyRequest> MarkPaidAsync(int requestId, CancellationToken cancellationToken = default)
        {
            var request = await _db.SupplyRequests.FindAsync(new object[] { requestId }, cancellationToken)
                ?? throw new InvalidOperationException($"Supply request {requestId} not found.");

            if (request.Stage != SupplyRequestStage.Invoiced)
            {
                throw new InvalidOperationException($"Request must be in Invoiced stage to mark as paid. Current: {request.Stage}");
            }

            request.Stage = SupplyRequestStage.Paid;
            request.PaidAtUtc = DateTime.UtcNow;
            request.UpdatedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);
            return request;
        }

        public async Task<SupplyRequest> MarkAuditedAsync(int requestId, CancellationToken cancellationToken = default)
        {
            var request = await _db.SupplyRequests.FindAsync(new object[] { requestId }, cancellationToken)
                ?? throw new InvalidOperationException($"Supply request {requestId} not found.");

            if (request.Stage != SupplyRequestStage.Paid)
            {
                throw new InvalidOperationException($"Request must be in Paid stage to mark as audited. Current: {request.Stage}");
            }

            request.Stage = SupplyRequestStage.Audited;
            request.AuditedAtUtc = DateTime.UtcNow;
            request.UpdatedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);
            return request;
        }

        public async Task<SupplyRequest> CancelAsync(int requestId, string? notes, CancellationToken cancellationToken = default)
        {
            var request = await _db.SupplyRequests.FindAsync(new object[] { requestId }, cancellationToken)
                ?? throw new InvalidOperationException($"Supply request {requestId} not found.");

            if (request.Stage >= SupplyRequestStage.ReceivedConfirmed)
            {
                throw new InvalidOperationException($"Cannot cancel request in {request.Stage} stage.");
            }

            request.Stage = SupplyRequestStage.Cancelled;
            request.Notes = string.IsNullOrWhiteSpace(notes) ? request.Notes : $"{request.Notes} | Cancelled: {notes}";
            request.UpdatedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync(cancellationToken);
            return request;
        }

        public async Task<SupplyRequestSummary> GetSummaryAsync(string? branchId, CancellationToken cancellationToken = default)
        {
            var query = _db.SupplyRequests.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(branchId))
            {
                query = query.Where(r => r.BranchId == branchId);
            }

            var startOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var monthlyQuery = query.Where(r => r.CreatedAtUtc >= startOfMonth);

            var pendingRequests = await query.CountAsync(r =>
                r.Stage != SupplyRequestStage.Audited &&
                r.Stage != SupplyRequestStage.Cancelled, cancellationToken);

            var awaitingApproval = await query.CountAsync(r =>
                r.Stage == SupplyRequestStage.Requested, cancellationToken);

            var inTransit = await query.CountAsync(r =>
                r.Stage == SupplyRequestStage.Ordered, cancellationToken);

            var readyForFinance = await query.CountAsync(r =>
                r.Stage == SupplyRequestStage.ReceivedConfirmed, cancellationToken);

            var totalThisMonth = await monthlyQuery.CountAsync(cancellationToken);

            var monthlyRequests = await monthlyQuery
                .Where(r => r.Stage != SupplyRequestStage.Cancelled)
                .ToListAsync(cancellationToken);

            var estimatedSpend = monthlyRequests.Sum(r =>
            {
                var qty = r.ReceivedQuantity ?? r.RequestedQuantity;
                var cost = r.ActualUnitCost ?? r.EstimatedUnitCost ?? 0m;
                return qty * cost;
            });

            return new SupplyRequestSummary(
                pendingRequests,
                awaitingApproval,
                inTransit,
                readyForFinance,
                totalThisMonth,
                estimatedSpend);
        }

        private static string GenerateRequestNumber()
        {
            return $"SR-{DateTime.UtcNow:yyMMdd}-{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}";
        }
    }
}
