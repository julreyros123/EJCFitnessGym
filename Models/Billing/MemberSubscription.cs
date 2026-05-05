using System.ComponentModel.DataAnnotations;

namespace EJCFitnessGym.Models.Billing
{
    public class MemberSubscription
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(450)]
        public string MemberUserId { get; set; } = string.Empty;

        public int SubscriptionPlanId { get; set; }

        public DateTime StartDateUtc { get; set; } = DateTime.UtcNow;

        public DateTime? EndDateUtc { get; set; }

        public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;

        [MaxLength(200)]
        public string? ExternalCustomerId { get; set; }

        [MaxLength(200)]
        public string? ExternalSubscriptionId { get; set; }

        public SubscriptionPlan? SubscriptionPlan { get; set; }
    }
}
