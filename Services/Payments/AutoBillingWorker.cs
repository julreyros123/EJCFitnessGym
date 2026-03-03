using Microsoft.Extensions.Options;

namespace EJCFitnessGym.Services.Payments
{
    public class AutoBillingWorkerOptions
    {
        /// <summary>
        /// Whether the auto-billing worker is enabled.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Interval between auto-billing runs in minutes.
        /// </summary>
        public int IntervalMinutes { get; set; } = 60;

        /// <summary>
        /// Whether to run auto-billing on startup.
        /// </summary>
        public bool RunOnStartup { get; set; } = false;

        /// <summary>
        /// The hour of day (0-23) to preferentially run auto-billing. 
        /// Auto-billing will still run at intervals but will prioritize this hour.
        /// </summary>
        public int PreferredHourUtc { get; set; } = 8; // 8 AM UTC = 4 PM PHT

        /// <summary>
        /// Maximum number of invoices to process per run.
        /// </summary>
        public int MaxInvoicesPerRun { get; set; } = 100;
    }

    public class AutoBillingWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IOptionsMonitor<AutoBillingWorkerOptions> _options;
        private readonly ILogger<AutoBillingWorker> _logger;

        public AutoBillingWorker(
            IServiceScopeFactory scopeFactory,
            IOptionsMonitor<AutoBillingWorkerOptions> options,
            ILogger<AutoBillingWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _options = options;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.CurrentValue.Enabled)
            {
                _logger.LogInformation("Auto-billing worker is disabled.");
                return;
            }

            _logger.LogInformation("Auto-billing worker started. Interval: {Interval} minutes.", _options.CurrentValue.IntervalMinutes);

            if (_options.CurrentValue.RunOnStartup)
            {
                await RunAutoBillingAsync("startup", stoppingToken);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                var intervalMinutes = Math.Clamp(_options.CurrentValue.IntervalMinutes, 15, 24 * 60);
                
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                await RunAutoBillingAsync("scheduled", stoppingToken);
            }

            _logger.LogInformation("Auto-billing worker stopped.");
        }

        private async Task RunAutoBillingAsync(string trigger, CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var autoBillingService = scope.ServiceProvider.GetRequiredService<IAutoBillingService>();

                _logger.LogDebug("Starting auto-billing run ({Trigger}).", trigger);

                var result = await autoBillingService.ProcessDueBillingAsync(cancellationToken);

                if (result.TotalDueInvoices > 0)
                {
                    _logger.LogInformation(
                        "Auto-billing run ({Trigger}) completed. Due: {DueInvoices}, Successful: {Successful}, Failed: {Failed}, Skipped: {Skipped}, Total charged: ₱{TotalCharged:N2}",
                        trigger,
                        result.TotalDueInvoices,
                        result.SuccessfulCharges,
                        result.FailedCharges,
                        result.SkippedInvoices,
                        result.TotalAmountCharged);
                }
                else
                {
                    _logger.LogDebug("Auto-billing run ({Trigger}) completed. No due invoices found.", trigger);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Graceful shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto-billing run ({Trigger}) failed with exception.", trigger);
            }
        }
    }
}
