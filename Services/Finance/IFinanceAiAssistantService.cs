namespace EJCFitnessGym.Services.Finance
{
    public interface IFinanceAiAssistantService
    {
        Task<FinanceAiOverviewDto> GetBranchAiOverviewAsync(
            string? branchId,
            DateTime? fromUtc = null,
            DateTime? toUtc = null,
            int priorityTake = 12,
            CancellationToken cancellationToken = default);

        Task<FinanceHighRiskAlertDispatchResultDto> DispatchNewHighRiskAlertsAsync(
            string trigger,
            DateTime? fromUtc = null,
            DateTime? toUtc = null,
            CancellationToken cancellationToken = default);
    }

    public sealed class FinanceAiOverviewDto
    {
        public string? BranchId { get; init; }

        public DateTime GeneratedAtUtc { get; init; }

        public DateTime FromUtc { get; init; }

        public DateTime ToUtc { get; init; }

        public int ScopedMemberCount { get; init; }

        public int HighRiskCount { get; init; }

        public int MediumRiskCount { get; init; }

        public int OverdueMemberCount { get; init; }

        public int OpenInvoiceCount { get; init; }

        public decimal OpenInvoiceExposureAmount { get; init; }

        public int RenewalsDueIn30DaysCount { get; init; }

        public IReadOnlyList<FinanceAiPriorityMemberItemDto> PriorityMembers { get; init; } =
            Array.Empty<FinanceAiPriorityMemberItemDto>();
    }

    public sealed class FinanceAiPriorityMemberItemDto
    {
        public string MemberUserId { get; init; } = string.Empty;

        public string MemberEmail { get; init; } = string.Empty;

        public string RiskLevel { get; init; } = string.Empty;

        public int RiskScore { get; init; }

        public string ReasonSummary { get; init; } = string.Empty;

        public DateTime? LastSuccessfulPaymentUtc { get; init; }

        public int OverdueInvoiceCount { get; init; }

        public decimal OverdueAmount { get; init; }

        public string SuggestedAction { get; init; } = string.Empty;
    }

    public sealed class FinanceHighRiskAlertDispatchResultDto
    {
        public int BranchesEvaluated { get; init; }

        public int HighRiskMembersEvaluated { get; init; }

        public int AlertsSent { get; init; }

        public DateTime EvaluatedAtUtc { get; init; }
    }
}
