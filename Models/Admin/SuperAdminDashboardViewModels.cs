namespace EJCFitnessGym.Models.Admin
{
    public sealed class SuperAdminDashboardViewModel
    {
        public DateTime AsOfUtc { get; init; }

        public string RevenuePeriodLabel { get; init; } = string.Empty;

        public int TotalUserCount { get; init; }

        public int TotalMemberCount { get; init; }

        public int ActiveMemberCount { get; init; }

        public int ActiveBranchCount { get; init; }

        public decimal RevenueThisMonth { get; init; }

        public int OpenInvoiceCount { get; init; }

        public decimal OpenInvoiceAmount { get; init; }

        public int NewAlertCount { get; init; }

        public int AcknowledgedAlertCount { get; init; }

        public int FailedOutboxCount { get; init; }

        public int PendingOutboxCount { get; init; }

        public DateTime? OldestPendingOutboxUtc { get; init; }

        public int UnassignedBackOfficeCount { get; init; }

        public int OpenRetentionActionCount { get; init; }

        public int HighChurnRiskCount { get; init; }

        public int MediumChurnRiskCount { get; init; }

        public IReadOnlyList<SuperAdminRoleCountItemViewModel> RoleCounts { get; init; } =
            Array.Empty<SuperAdminRoleCountItemViewModel>();

        public IReadOnlyList<SuperAdminBranchLoadItemViewModel> BranchLoads { get; init; } =
            Array.Empty<SuperAdminBranchLoadItemViewModel>();

        public IReadOnlyList<SuperAdminPrivilegedUserItemViewModel> PrivilegedUsers { get; init; } =
            Array.Empty<SuperAdminPrivilegedUserItemViewModel>();

        public IReadOnlyList<SuperAdminRecentAlertItemViewModel> RecentAlerts { get; init; } =
            Array.Empty<SuperAdminRecentAlertItemViewModel>();

        public IReadOnlyList<SuperAdminMemberSegmentDistributionItemViewModel> MemberSegmentDistribution { get; init; } =
            Array.Empty<SuperAdminMemberSegmentDistributionItemViewModel>();

        public IReadOnlyList<SuperAdminRetentionActionItemViewModel> RetentionActions { get; init; } =
            Array.Empty<SuperAdminRetentionActionItemViewModel>();

        public IReadOnlyList<SuperAdminChurnRiskMemberItemViewModel> AtRiskMembers { get; init; } =
            Array.Empty<SuperAdminChurnRiskMemberItemViewModel>();
    }

    public sealed class SuperAdminRoleCountItemViewModel
    {
        public string RoleName { get; init; } = string.Empty;

        public int UserCount { get; init; }
    }

    public sealed class SuperAdminBranchLoadItemViewModel
    {
        public string BranchId { get; init; } = string.Empty;

        public int UserCount { get; init; }
    }

    public sealed class SuperAdminPrivilegedUserItemViewModel
    {
        public string Email { get; init; } = string.Empty;

        public string RolesSummary { get; init; } = string.Empty;

        public string? BranchId { get; init; }

        public bool RequiresBranch { get; init; }

        public bool MissingBranch { get; init; }
    }

    public sealed class SuperAdminRecentAlertItemViewModel
    {
        public DateTime CreatedUtc { get; init; }

        public string AlertType { get; init; } = string.Empty;

        public string Severity { get; init; } = string.Empty;

        public string State { get; init; } = string.Empty;

        public string Message { get; init; } = string.Empty;
    }

    public sealed class SuperAdminMemberSegmentDistributionItemViewModel
    {
        public string SegmentLabel { get; init; } = string.Empty;

        public int MemberCount { get; init; }
    }

    public sealed class SuperAdminRetentionActionItemViewModel
    {
        public string MemberEmail { get; init; } = string.Empty;

        public string ActionType { get; init; } = string.Empty;

        public string SegmentLabel { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public string Reason { get; init; } = string.Empty;

        public DateTime? DueDateUtc { get; init; }
    }

    public sealed class SuperAdminChurnRiskMemberItemViewModel
    {
        public string MemberEmail { get; init; } = string.Empty;

        public int RiskScore { get; init; }

        public string RiskLevel { get; init; } = string.Empty;

        public string ReasonSummary { get; init; } = string.Empty;

        public DateTime? LastPaymentUtc { get; init; }
    }
}
