using Microsoft.Extensions.Options;

namespace EJCFitnessGym.Services.Finance
{
    public class FinanceAlertEvaluatorWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IOptionsMonitor<FinanceAlertEvaluatorOptions> _options;
        private readonly ILogger<FinanceAlertEvaluatorWorker> _logger;

        public FinanceAlertEvaluatorWorker(
            IServiceScopeFactory scopeFactory,
            IOptionsMonitor<FinanceAlertEvaluatorOptions> options,
            ILogger<FinanceAlertEvaluatorWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _options = options;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_options.CurrentValue.Enabled && _options.CurrentValue.RunOnStartup)
            {
                await RunEvaluationAsync("startup", stoppingToken);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                var intervalMinutes = NormalizeIntervalMinutes(_options.CurrentValue.IntervalMinutes);
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                if (!_options.CurrentValue.Enabled)
                {
                    _logger.LogDebug("Finance alert evaluator worker is disabled. Skipping cycle.");
                    continue;
                }

                await RunEvaluationAsync("scheduled", stoppingToken);
            }
        }

        private async Task RunEvaluationAsync(string trigger, CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var financeAlertService = scope.ServiceProvider.GetRequiredService<IFinanceAlertService>();
                var financeAiAssistantService = scope.ServiceProvider.GetRequiredService<IFinanceAiAssistantService>();

                var result = await financeAlertService.EvaluateAndNotifyAsync(
                    $"finance.alerts.worker.{trigger}",
                    cancellationToken);
                var churnResult = await financeAiAssistantService.DispatchNewHighRiskAlertsAsync(
                    $"finance.alerts.worker.{trigger}",
                    cancellationToken: cancellationToken);

                if (!result.Enabled)
                {
                    _logger.LogDebug(
                        "Finance alert evaluation ({Trigger}) ran with alerts disabled at {EvaluatedAtUtc}.",
                        trigger,
                        result.EvaluatedAtUtc);
                    return;
                }

                if (result.AlertsSent > 0)
                {
                    _logger.LogInformation(
                        "Finance alert evaluation ({Trigger}) sent {AlertsSent} finance alert(s) + {ChurnAlertsSent} churn alert(s). RiskLevel={RiskLevel}, HighSeverityAnomalies={HighSeverityAnomalies}, EvaluatedAtUtc={EvaluatedAtUtc}.",
                        trigger,
                        result.AlertsSent,
                        churnResult.AlertsSent,
                        result.RiskLevel,
                        result.HighSeverityAnomalies,
                        result.EvaluatedAtUtc);
                }
                else
                {
                    _logger.LogDebug(
                        "Finance alert evaluation ({Trigger}) sent no finance alerts; churn alerts sent: {ChurnAlertsSent}. RiskLevel={RiskLevel}, HighSeverityAnomalies={HighSeverityAnomalies}, EvaluatedAtUtc={EvaluatedAtUtc}.",
                        trigger,
                        churnResult.AlertsSent,
                        result.RiskLevel,
                        result.HighSeverityAnomalies,
                        result.EvaluatedAtUtc);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Graceful shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Finance alert evaluation ({Trigger}) failed.", trigger);
            }
        }

        private static int NormalizeIntervalMinutes(int configuredIntervalMinutes)
        {
            return Math.Clamp(configuredIntervalMinutes, 5, 24 * 60);
        }
    }
}
