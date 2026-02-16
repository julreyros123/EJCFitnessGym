namespace EJCFitnessGym.Services.Monitoring
{
    public class OperationalHealthOptions
    {
        public int PendingOutboxWarningThreshold { get; set; } = 150;

        public int PendingOutboxCriticalThreshold { get; set; } = 500;

        public int PendingOutboxOldestWarningMinutes { get; set; } = 10;

        public int PendingOutboxOldestCriticalMinutes { get; set; } = 30;

        public int FailedOutboxWarningThreshold { get; set; } = 5;

        public int FailedOutboxCriticalThreshold { get; set; } = 20;

        public int FailedWebhookWarningThreshold { get; set; } = 5;

        public int FailedWebhookCriticalThreshold { get; set; } = 20;
    }
}
