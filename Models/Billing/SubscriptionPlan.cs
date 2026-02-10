using System.ComponentModel.DataAnnotations;

namespace EJCFitnessGym.Models.Billing
{
    public class SubscriptionPlan
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        [Range(0, 999999)]
        public decimal Price { get; set; }

        public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
