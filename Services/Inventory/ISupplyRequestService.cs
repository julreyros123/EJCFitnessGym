using EJCFitnessGym.Models.Inventory;
using EJCFitnessGym.Models.Finance;

namespace EJCFitnessGym.Services.Inventory
{
    public interface ISupplyRequestService
    {
        Task<SupplyRequest> CreateRequestAsync(SupplyRequest request, CancellationToken cancellationToken = default);
        Task<SupplyRequest?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
        Task<SupplyRequest?> GetByRequestNumberAsync(string requestNumber, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<SupplyRequest>> GetRequestsAsync(string? branchId, SupplyRequestStage? stage = null, int take = 100, CancellationToken cancellationToken = default);

        Task<SupplyRequest> ApproveAsync(int requestId, string approvedByUserId, CancellationToken cancellationToken = default);
        Task<SupplyRequest> MarkOrderedAsync(int requestId, CancellationToken cancellationToken = default);
        Task<SupplyRequest> ReceiveDraftAsync(int requestId, int receivedQuantity, decimal actualUnitCost, string receivedByUserId, CancellationToken cancellationToken = default);
        Task<SupplyRequest> ConfirmReceiptAsync(int requestId, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Creates a finance expense record and links it to the supply request.
        /// This moves the request to the Invoiced stage.
        /// </summary>
        Task<(SupplyRequest Request, FinanceExpenseRecord Expense)> CreateExpenseAsync(int requestId, string? createdByUserId, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Marks the supply request as Paid after the expense is paid.
        /// </summary>
        Task<SupplyRequest> MarkPaidAsync(int requestId, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Marks the supply request as Audited after finance review.
        /// </summary>
        Task<SupplyRequest> MarkAuditedAsync(int requestId, CancellationToken cancellationToken = default);
        
        Task<SupplyRequest> CancelAsync(int requestId, string? notes, CancellationToken cancellationToken = default);

        Task<SupplyRequestSummary> GetSummaryAsync(string? branchId, CancellationToken cancellationToken = default);
    }

    public record SupplyRequestSummary(
        int PendingRequests,
        int AwaitingApproval,
        int InTransit,
        int ReadyForFinance,
        int TotalThisMonth,
        decimal EstimatedMonthlySpend);
}
