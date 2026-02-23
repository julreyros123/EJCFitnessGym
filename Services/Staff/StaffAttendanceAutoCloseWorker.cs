using Microsoft.Extensions.Options;

namespace EJCFitnessGym.Services.Staff
{
    public sealed class StaffAttendanceAutoCloseWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IOptionsMonitor<StaffAttendanceOptions> _optionsMonitor;
        private readonly ILogger<StaffAttendanceAutoCloseWorker> _logger;

        public StaffAttendanceAutoCloseWorker(
            IServiceScopeFactory scopeFactory,
            IOptionsMonitor<StaffAttendanceOptions> optionsMonitor,
            ILogger<StaffAttendanceAutoCloseWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _optionsMonitor = optionsMonitor;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_optionsMonitor.CurrentValue.AutoCheckoutEnabled)
            {
                _logger.LogInformation("Staff attendance auto-close worker is disabled.");
                return;
            }

            if (_optionsMonitor.CurrentValue.RunOnStartup)
            {
                await SweepAsync("startup", stoppingToken);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                var delayMinutes = Math.Clamp(_optionsMonitor.CurrentValue.AutoCloseIntervalMinutes, 1, 60);

                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(delayMinutes), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                await SweepAsync("scheduled", stoppingToken);
            }
        }

        private async Task SweepAsync(string trigger, CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var attendanceService = scope.ServiceProvider.GetRequiredService<IStaffAttendanceService>();
                var autoClosedCount = await attendanceService.AutoCloseStaleSessionsAsync(cancellationToken: cancellationToken);

                if (autoClosedCount > 0)
                {
                    _logger.LogInformation(
                        "Staff attendance auto-close worker ({Trigger}) closed {Count} stale session(s).",
                        trigger,
                        autoClosedCount);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Graceful shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Staff attendance auto-close worker ({Trigger}) failed.", trigger);
            }
        }
    }
}
