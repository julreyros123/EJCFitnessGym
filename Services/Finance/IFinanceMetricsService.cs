using EJCFitnessGym.Models.Finance;

namespace EJCFitnessGym.Services.Finance
{
    public interface IFinanceMetricsService
    {
        Task<FinanceOverviewDto> GetOverviewAsync(
            DateTime? fromUtc = null,
            DateTime? toUtc = null,
            CancellationToken cancellationToken = default);

        Task<FinanceInsightsDto> GetInsightsAsync(
            int lookbackDays = 120,
            int forecastDays = 30,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<GymEquipmentAsset>> GetEquipmentAssetsAsync(CancellationToken cancellationToken = default);

        Task<IReadOnlyList<FinanceExpenseRecord>> GetExpensesAsync(
            DateTime? fromUtc = null,
            DateTime? toUtc = null,
            CancellationToken cancellationToken = default);

        Task<EquipmentSeedResultDto> SeedMediumGymSampleAsync(CancellationToken cancellationToken = default);
    }

    public sealed class FinanceOverviewDto
    {
        public DateTime FromUtc { get; init; }
        public DateTime ToUtc { get; init; }
        public DateTime ComputedAtUtc { get; init; }
        public int SuccessfulPaymentsCount { get; init; }
        public decimal TotalRevenue { get; init; }
        public decimal PayMongoRevenue { get; init; }
        public decimal OperatingExpenses { get; init; }
        public decimal TotalCosts { get; init; }
        public int EquipmentAssetItemCount { get; init; }
        public int EquipmentTotalUnits { get; init; }
        public decimal EquipmentTotalInvestment { get; init; }
        public decimal EquipmentMonthlyDepreciation { get; init; }
        public decimal EstimatedNetProfit { get; init; }
        public decimal? EquipmentPaybackPercent { get; init; }
    }

    public sealed class EquipmentSeedResultDto
    {
        public int InsertedCount { get; init; }
        public int SkippedCount { get; init; }
        public int TotalAssets { get; init; }
    }

    public sealed class FinanceInsightsDto
    {
        public DateTime GeneratedAtUtc { get; init; }
        public DateTime LookbackFromUtc { get; init; }
        public DateTime LookbackToUtc { get; init; }
        public int LookbackDays { get; init; }
        public int ForecastDays { get; init; }
        public decimal LookbackRevenue { get; init; }
        public decimal AverageDailyRevenue { get; init; }
        public decimal ForecastRevenue { get; init; }
        public decimal ForecastNet { get; init; }
        public decimal LookbackOperatingExpenses { get; init; }
        public decimal AverageDailyOperatingExpense { get; init; }
        public decimal ForecastOperatingExpense { get; init; }
        public decimal ForecastTotalExpense { get; init; }
        public decimal ForecastDepreciationCost { get; init; }
        public decimal? ForecastChangePercent { get; init; }
        public string GainOrLossSignal { get; init; } = "Neutral";
        public string RiskLevel { get; init; } = "Low";
        public IReadOnlyList<FinanceAnomalyDto> Anomalies { get; init; } = Array.Empty<FinanceAnomalyDto>();
    }

    public sealed class FinanceAnomalyDto
    {
        public DateTime DateUtc { get; init; }
        public string Type { get; init; } = string.Empty;
        public decimal ActualValue { get; init; }
        public decimal ExpectedValue { get; init; }
        public decimal? DeviationPercent { get; init; }
        public string Severity { get; init; } = "Low";
        public string Description { get; init; } = string.Empty;
    }
}
