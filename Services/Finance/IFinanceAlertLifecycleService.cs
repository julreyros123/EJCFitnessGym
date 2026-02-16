using EJCFitnessGym.Models.Finance;

namespace EJCFitnessGym.Services.Finance
{
    public interface IFinanceAlertLifecycleService
    {
        Task<FinanceAlertLifecycleResult> AcknowledgeAsync(
            int alertId,
            string actor,
            CancellationToken cancellationToken = default);

        Task<FinanceAlertLifecycleResult> ResolveAsync(
            int alertId,
            string actor,
            bool falsePositive = false,
            string? resolutionNote = null,
            CancellationToken cancellationToken = default);

        Task<FinanceAlertLifecycleResult> ReopenAsync(
            int alertId,
            string actor,
            CancellationToken cancellationToken = default);
    }

    public sealed class FinanceAlertLifecycleResult
    {
        public bool Found { get; init; }
        public bool Changed { get; init; }
        public bool InvalidTransition { get; init; }
        public string Message { get; init; } = string.Empty;
        public FinanceAlertLog? Alert { get; init; }
    }
}
