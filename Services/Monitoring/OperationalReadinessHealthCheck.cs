using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EJCFitnessGym.Services.Monitoring
{
    public class OperationalReadinessHealthCheck : IHealthCheck
    {
        private readonly ApplicationDbContext _db;
        private readonly OperationalHealthOptions _options;

        public OperationalReadinessHealthCheck(
            ApplicationDbContext db,
            IOptions<OperationalHealthOptions> options)
        {
            _db = db;
            _options = options.Value;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            var nowUtc = DateTime.UtcNow;
            try
            {
                var canConnect = await _db.Database.CanConnectAsync(cancellationToken);
                if (!canConnect)
                {
                    return HealthCheckResult.Unhealthy("Database connection failed.");
                }

                var pendingOrProcessing = await _db.IntegrationOutboxMessages
                    .Where(m => m.Status == IntegrationOutboxStatus.Pending || m.Status == IntegrationOutboxStatus.Processing)
                    .GroupBy(_ => 1)
                    .Select(g => new
                    {
                        Count = g.Count(),
                        OldestCreatedUtc = g.Min(m => m.CreatedUtc)
                    })
                    .FirstOrDefaultAsync(cancellationToken);

                var pendingOutboxCount = pendingOrProcessing?.Count ?? 0;
                var oldestPendingCreatedUtc = pendingOrProcessing?.OldestCreatedUtc;
                var oldestPendingAgeMinutes = oldestPendingCreatedUtc.HasValue
                    ? Math.Max(0d, (nowUtc - oldestPendingCreatedUtc.Value).TotalMinutes)
                    : 0d;

                var failedOutboxCount = await _db.IntegrationOutboxMessages
                    .CountAsync(m => m.Status == IntegrationOutboxStatus.Failed, cancellationToken);

                var failedWebhookReceiptCount = await _db.InboundWebhookReceipts
                    .CountAsync(r => r.Status == "Failed", cancellationToken);

                var isCritical =
                    pendingOutboxCount >= _options.PendingOutboxCriticalThreshold ||
                    oldestPendingAgeMinutes >= _options.PendingOutboxOldestCriticalMinutes ||
                    failedOutboxCount >= _options.FailedOutboxCriticalThreshold ||
                    failedWebhookReceiptCount >= _options.FailedWebhookCriticalThreshold;

                var isWarning =
                    pendingOutboxCount >= _options.PendingOutboxWarningThreshold ||
                    oldestPendingAgeMinutes >= _options.PendingOutboxOldestWarningMinutes ||
                    failedOutboxCount >= _options.FailedOutboxWarningThreshold ||
                    failedWebhookReceiptCount >= _options.FailedWebhookWarningThreshold;

                var data = new Dictionary<string, object>
                {
                    ["database.canConnect"] = true,
                    ["outbox.pending.count"] = pendingOutboxCount,
                    ["outbox.pending.oldestCreatedUtc"] = oldestPendingCreatedUtc?.ToString("O") ?? "none",
                    ["outbox.pending.oldestAgeMinutes"] = Math.Round(oldestPendingAgeMinutes, 2),
                    ["outbox.failed.count"] = failedOutboxCount,
                    ["webhook.failed.count"] = failedWebhookReceiptCount,
                    ["threshold.pending.warning"] = _options.PendingOutboxWarningThreshold,
                    ["threshold.pending.critical"] = _options.PendingOutboxCriticalThreshold,
                    ["threshold.pendingOldestMinutes.warning"] = _options.PendingOutboxOldestWarningMinutes,
                    ["threshold.pendingOldestMinutes.critical"] = _options.PendingOutboxOldestCriticalMinutes,
                    ["threshold.failedOutbox.warning"] = _options.FailedOutboxWarningThreshold,
                    ["threshold.failedOutbox.critical"] = _options.FailedOutboxCriticalThreshold,
                    ["threshold.failedWebhook.warning"] = _options.FailedWebhookWarningThreshold,
                    ["threshold.failedWebhook.critical"] = _options.FailedWebhookCriticalThreshold
                };

                if (isCritical)
                {
                    return HealthCheckResult.Unhealthy(
                        "Operational readiness is unhealthy: critical thresholds exceeded.",
                        data: data);
                }

                if (isWarning)
                {
                    return HealthCheckResult.Degraded(
                        "Operational readiness is degraded: warning thresholds exceeded.",
                        data: data);
                }

                return HealthCheckResult.Healthy(
                    "Operational readiness is healthy.",
                    data);
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy(
                    "Operational readiness check failed.",
                    ex);
            }
        }
    }
}
