using EJCFitnessGym.Models.Billing;

namespace EJCFitnessGym.Services.Memberships
{
    public interface IMembershipService
    {
        Task<MemberSubscription?> GetLatestSubscriptionAsync(string memberUserId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<MemberSubscription>> GetSubscriptionHistoryAsync(string memberUserId, int take = 12, CancellationToken cancellationToken = default);

        Task<MemberSubscription> ActivateSubscriptionAsync(
            string memberUserId,
            int planId,
            DateTime? startDateUtc = null,
            string? externalSubscriptionId = null,
            string? externalCustomerId = null,
            CancellationToken cancellationToken = default);

        Task<MemberSubscription?> ResumeSubscriptionAsync(
            string memberUserId,
            DateTime? resumeAtUtc = null,
            CancellationToken cancellationToken = default);

        Task<MembershipLifecycleMaintenanceResult> RunLifecycleMaintenanceAsync(
            DateTime? asOfUtc = null,
            CancellationToken cancellationToken = default);
    }

    public sealed class MembershipLifecycleMaintenanceResult
    {
        public DateTime AsOfUtc { get; init; }
        public int ExpiredSubscriptions { get; init; }
        public int OverdueInvoices { get; init; }
    }
}
