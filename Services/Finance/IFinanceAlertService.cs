namespace EJCFitnessGym.Services.Finance
{
    public interface IFinanceAlertService
    {
        Task<FinanceAlertEvaluationResultDto> EvaluateAndNotifyAsync(
            string trigger,
            CancellationToken cancellationToken = default);
    }

    public sealed class FinanceAlertEvaluationResultDto
    {
        public bool Enabled { get; init; }
        public string Trigger { get; init; } = string.Empty;
        public int AlertsSent { get; init; }
        public bool RiskAlertSent { get; init; }
        public bool AnomalyAlertSent { get; init; }
        public string RiskLevel { get; init; } = "Low";
        public int HighSeverityAnomalies { get; init; }
        public DateTime EvaluatedAtUtc { get; init; }
    }
}
