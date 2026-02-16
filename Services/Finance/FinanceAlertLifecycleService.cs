using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Finance;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Services.Finance
{
    public class FinanceAlertLifecycleService : IFinanceAlertLifecycleService
    {
        private readonly ApplicationDbContext _db;

        public FinanceAlertLifecycleService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<FinanceAlertLifecycleResult> AcknowledgeAsync(
            int alertId,
            string actor,
            CancellationToken cancellationToken = default)
        {
            var alert = await _db.FinanceAlertLogs
                .FirstOrDefaultAsync(l => l.Id == alertId, cancellationToken);

            if (alert is null)
            {
                return new FinanceAlertLifecycleResult
                {
                    Found = false,
                    Changed = false,
                    InvalidTransition = false,
                    Message = "Finance alert not found."
                };
            }

            if (alert.State == FinanceAlertState.Resolved || alert.State == FinanceAlertState.FalsePositive)
            {
                return new FinanceAlertLifecycleResult
                {
                    Found = true,
                    Changed = false,
                    InvalidTransition = true,
                    Message = "Cannot acknowledge a closed alert.",
                    Alert = alert
                };
            }

            if (alert.State == FinanceAlertState.Acknowledged)
            {
                return new FinanceAlertLifecycleResult
                {
                    Found = true,
                    Changed = false,
                    InvalidTransition = false,
                    Message = "Alert is already acknowledged.",
                    Alert = alert
                };
            }

            var nowUtc = DateTime.UtcNow;
            alert.State = FinanceAlertState.Acknowledged;
            alert.StateUpdatedUtc = nowUtc;
            alert.AcknowledgedUtc = nowUtc;
            alert.AcknowledgedBy = NormalizeActor(actor);

            await _db.SaveChangesAsync(cancellationToken);

            return new FinanceAlertLifecycleResult
            {
                Found = true,
                Changed = true,
                InvalidTransition = false,
                Message = "Alert acknowledged.",
                Alert = alert
            };
        }

        public async Task<FinanceAlertLifecycleResult> ResolveAsync(
            int alertId,
            string actor,
            bool falsePositive = false,
            string? resolutionNote = null,
            CancellationToken cancellationToken = default)
        {
            var alert = await _db.FinanceAlertLogs
                .FirstOrDefaultAsync(l => l.Id == alertId, cancellationToken);

            if (alert is null)
            {
                return new FinanceAlertLifecycleResult
                {
                    Found = false,
                    Changed = false,
                    InvalidTransition = false,
                    Message = "Finance alert not found."
                };
            }

            var targetState = falsePositive
                ? FinanceAlertState.FalsePositive
                : FinanceAlertState.Resolved;

            if (alert.State == targetState)
            {
                return new FinanceAlertLifecycleResult
                {
                    Found = true,
                    Changed = false,
                    InvalidTransition = false,
                    Message = falsePositive
                        ? "Alert is already marked as false positive."
                        : "Alert is already resolved.",
                    Alert = alert
                };
            }

            var nowUtc = DateTime.UtcNow;
            var normalizedActor = NormalizeActor(actor);

            if (alert.AcknowledgedUtc is null)
            {
                alert.AcknowledgedUtc = nowUtc;
                alert.AcknowledgedBy = normalizedActor;
            }

            alert.State = targetState;
            alert.StateUpdatedUtc = nowUtc;
            alert.ResolvedUtc = nowUtc;
            alert.ResolvedBy = normalizedActor;
            alert.ResolutionNote = NormalizeResolutionNote(resolutionNote);

            await _db.SaveChangesAsync(cancellationToken);

            return new FinanceAlertLifecycleResult
            {
                Found = true,
                Changed = true,
                InvalidTransition = false,
                Message = falsePositive
                    ? "Alert marked as false positive."
                    : "Alert resolved.",
                Alert = alert
            };
        }

        public async Task<FinanceAlertLifecycleResult> ReopenAsync(
            int alertId,
            string actor,
            CancellationToken cancellationToken = default)
        {
            var alert = await _db.FinanceAlertLogs
                .FirstOrDefaultAsync(l => l.Id == alertId, cancellationToken);

            if (alert is null)
            {
                return new FinanceAlertLifecycleResult
                {
                    Found = false,
                    Changed = false,
                    InvalidTransition = false,
                    Message = "Finance alert not found."
                };
            }

            if (alert.State == FinanceAlertState.New)
            {
                return new FinanceAlertLifecycleResult
                {
                    Found = true,
                    Changed = false,
                    InvalidTransition = false,
                    Message = "Alert is already open.",
                    Alert = alert
                };
            }

            var nowUtc = DateTime.UtcNow;
            alert.State = FinanceAlertState.New;
            alert.StateUpdatedUtc = nowUtc;
            alert.AcknowledgedUtc = null;
            alert.AcknowledgedBy = null;
            alert.ResolvedUtc = null;
            alert.ResolvedBy = null;
            alert.ResolutionNote = null;

            await _db.SaveChangesAsync(cancellationToken);

            return new FinanceAlertLifecycleResult
            {
                Found = true,
                Changed = true,
                InvalidTransition = false,
                Message = $"Alert reopened by {NormalizeActor(actor)}.",
                Alert = alert
            };
        }

        private static string NormalizeActor(string? actor)
        {
            if (string.IsNullOrWhiteSpace(actor))
            {
                return "unknown";
            }

            return actor.Trim();
        }

        private static string? NormalizeResolutionNote(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var trimmed = value.Trim();
            return trimmed.Length <= 500 ? trimmed : trimmed[..500];
        }
    }
}
