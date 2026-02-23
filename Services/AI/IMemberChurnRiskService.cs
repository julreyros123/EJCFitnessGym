namespace EJCFitnessGym.Services.AI
{
    public sealed class MemberChurnRiskInput
    {
        public string MemberUserId { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public float TotalSpending { get; init; }

        public float BillingActivityCount { get; init; }

        public float MembershipMonths { get; init; }

        public float? DaysSinceLastSuccessfulPayment { get; init; }

        public float? DaysUntilMembershipEnd { get; init; }

        public int OverdueInvoiceCount { get; init; }

        public bool HasActiveMembership { get; init; }
    }

    public sealed class MemberChurnRiskResult
    {
        public string MemberUserId { get; init; } = string.Empty;

        public int RiskScore { get; init; }

        public string RiskLevel { get; init; } = string.Empty;

        public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();

        public string ReasonSummary { get; init; } = string.Empty;
    }

    public sealed class MemberChurnRiskSummaryItem
    {
        public string RiskLevel { get; init; } = string.Empty;

        public int MemberCount { get; init; }
    }

    public sealed class MemberChurnRiskBatchResult
    {
        public IReadOnlyDictionary<string, MemberChurnRiskResult> ResultsByMemberId { get; init; } =
            new Dictionary<string, MemberChurnRiskResult>(StringComparer.Ordinal);

        public IReadOnlyList<MemberChurnRiskSummaryItem> LevelSummary { get; init; } =
            Array.Empty<MemberChurnRiskSummaryItem>();
    }

    public interface IMemberChurnRiskService
    {
        MemberChurnRiskBatchResult PredictRisk(IReadOnlyList<MemberChurnRiskInput> members);
    }
}
