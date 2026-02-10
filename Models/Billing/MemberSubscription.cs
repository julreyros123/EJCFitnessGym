using System.ComponentModel.DataAnnotations;

namespace EJCFitnessGym.Models.Billing
{
    public class MemberSubscription
    {
        public int Id { get; set; }

        [Required]
        public string MemberUserId { get; set; } = string.Empty;

        public int SubscriptionPlanId { get; set; }

        public DateTime StartDateUtc { get; set; } = DateTime.UtcNow;

        public DateTime? EndDateUtc { get; set; }

        public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;

        public string? ExternalCustomerId { get; set; }

        public string? ExternalSubscriptionId { get; set; }

        public SubscriptionPlan? SubscriptionPlan { get; set; }
    }
}
