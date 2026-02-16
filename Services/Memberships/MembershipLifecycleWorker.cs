using EJCFitnessGym.Services.Realtime;
using Microsoft.Extensions.Options;

namespace EJCFitnessGym.Services.Memberships
{
    public class MembershipLifecycleWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IOptionsMonitor<MembershipLifecycleWorkerOptions> _options;
        private readonly ILogger<MembershipLifecycleWorker> _logger;

        public MembershipLifecycleWorker(
            IServiceScopeFactory scopeFactory,
            IOptionsMonitor<MembershipLifecycleWorkerOptions> options,
            ILogger<MembershipLifecycleWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _options = options;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_options.CurrentValue.Enabled)
            {
                _logger.LogInformation("Membership lifecycle worker is disabled.");
                return;
            }

            if (_options.CurrentValue.RunOnStartup)
            {
                await RunMaintenanceAsync("startup", stoppingToken);
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

                await RunMaintenanceAsync("scheduled", stoppingToken);
            }
        }

        private async Task RunMaintenanceAsync(string trigger, CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var membershipService = scope.ServiceProvider.GetRequiredService<IMembershipService>();
                var eventPublisher = scope.ServiceProvider.GetService<IErpEventPublisher>();

                var result = await membershipService.RunLifecycleMaintenanceAsync(cancellationToken: cancellationToken);
                var totalChanges = result.ExpiredSubscriptions + result.OverdueInvoices;

                if (totalChanges <= 0)
                {
                    _logger.LogDebug(
                        "Membership lifecycle maintenance ({Trigger}) completed with no changes at {AsOfUtc}.",
                        trigger,
                        result.AsOfUtc);
                    return;
                }

                _logger.LogInformation(
                    "Membership lifecycle maintenance ({Trigger}) updated {ExpiredSubscriptions} expired subscriptions and {OverdueInvoices} overdue invoices at {AsOfUtc}.",
                    trigger,
                    result.ExpiredSubscriptions,
                    result.OverdueInvoices,
                    result.AsOfUtc);

                if (_options.CurrentValue.PublishRealtimeWhenChangesDetected && eventPublisher is not null)
                {
                    await eventPublisher.PublishToBackOfficeAsync(
                        "membership.lifecycle.maintenance",
                        "Membership lifecycle maintenance updated subscription/invoice states.",
                        new
                        {
                            trigger,
                            asOfUtc = result.AsOfUtc,
                            expiredSubscriptions = result.ExpiredSubscriptions,
                            overdueInvoices = result.OverdueInvoices
                        },
                        cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Graceful shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Membership lifecycle maintenance ({Trigger}) failed.", trigger);
            }
        }

        private static int NormalizeIntervalMinutes(int configuredIntervalMinutes)
        {
            return Math.Clamp(configuredIntervalMinutes, 5, 24 * 60);
        }
    }
}
