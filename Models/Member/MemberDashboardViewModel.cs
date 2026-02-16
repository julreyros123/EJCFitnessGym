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
        public int TotalInvoices { get; set; }
        public int PaidInvoiceCount { get; set; }
        public int OpenInvoiceCount { get; set; }
        public int ProfileCompletionPercent { get; set; }
    }
}
