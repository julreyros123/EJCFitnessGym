using EJCFitnessGym.Models.Finance;

namespace EJCFitnessGym.Services.Finance
{
    public interface IGeneralLedgerService
    {
        Task EnsureDefaultAccountsAsync(string? branchId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<GeneralLedgerAccount>> GetActiveAccountsAsync(
            string branchId,
            CancellationToken cancellationToken = default);

        Task PostPaymentReceiptAsync(
            int paymentId,
            string? actorUserId = null,
            CancellationToken cancellationToken = default);

        Task PostOperatingExpenseAsync(
            int expenseId,
            string? actorUserId = null,
            CancellationToken cancellationToken = default);

        Task<GeneralLedgerEntry> CreateManualEntryAsync(
            string branchId,
            DateTime entryDateUtc,
            string description,
            int debitAccountId,
            int creditAccountId,
            decimal amount,
            string? memo = null,
            string? actorUserId = null,
            CancellationToken cancellationToken = default);
    }
}
