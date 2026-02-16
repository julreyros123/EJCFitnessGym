namespace EJCFitnessGym.Services.Integration
{
    public class IntegrationOutboxDispatcherOptions
    {
        public bool Enabled { get; set; } = true;
        public int PollIntervalSeconds { get; set; } = 5;
        public int BatchSize { get; set; } = 100;
        public int MaxAttempts { get; set; } = 10;
        public int BaseRetryDelaySeconds { get; set; } = 10;
    }
}
