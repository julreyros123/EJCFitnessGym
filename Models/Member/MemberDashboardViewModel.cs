namespace EJCFitnessGym.Models.Member
{
    public class MemberDashboardViewModel
    {
        public string MemberDisplayName { get; set; } = "Member";
        public string CurrentPlanName { get; set; } = "No plan selected";
        public string MembershipStatusLabel { get; set; } = "No Active Plan";
        public string MembershipStatusBadgeClass { get; set; } = "bg-secondary";
        public bool HasSubscriptionRecord { get; set; }
        public bool HasActiveMembership { get; set; }
        public DateTime? MembershipStartDateUtc { get; set; }
        public DateTime? MembershipEndDateUtc { get; set; }
        public DateTime? NextPaymentDueDateUtc { get; set; }
        public decimal LifetimeSpend { get; set; }
        public decimal OutstandingBalance { get; set; }
        public decimal ScheduledBalance { get; set; }
        public int TotalInvoices { get; set; }
        public int PaidInvoiceCount { get; set; }
        public int OpenInvoiceCount { get; set; }
        public int PendingInvoiceCount { get; set; }
        public int ExpiredInvoiceCount { get; set; }
        public int UpcomingInvoiceCount { get; set; }
        public int ProfileCompletionPercent { get; set; }
        public string MemberCheckInCode { get; set; } = string.Empty;
        public string MemberQrPayload { get; set; } = string.Empty;
        public IReadOnlyList<MemberActivityItemViewModel> RecentActivities { get; set; } = Array.Empty<MemberActivityItemViewModel>();
    }

    public class MemberActivityItemViewModel
    {
        public DateTime EventUtc { get; set; }
        public string EventType { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
