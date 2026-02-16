namespace EJCFitnessGym.Services.Finance
{
    public class FinanceAlertOptions
    {
        public bool Enabled { get; set; } = true;
        public bool EmailEnabled { get; set; }
        public string[] EmailRecipients { get; set; } = Array.Empty<string>();
        public int CooldownMinutes { get; set; } = 120;
        public int LookbackDays { get; set; } = 120;
        public int ForecastDays { get; set; } = 30;
        public int MinHighSeverityAnomalies { get; set; } = 1;
    }
}
