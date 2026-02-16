namespace EJCFitnessGym.Services.Finance
{
    public class FinanceAlertEvaluatorOptions
    {
        public bool Enabled { get; set; } = true;
        public bool RunOnStartup { get; set; } = true;
        public int IntervalMinutes { get; set; } = 30;
    }
}
