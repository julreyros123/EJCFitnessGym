namespace EJCFitnessGym.Services.Payments
{
    public interface IPayMongoMembershipReconciliationService
    {
        Task<int> ReconcilePendingMemberPaymentsAsync(
            string memberUserId,
            CancellationToken cancellationToken = default);
    }
}
