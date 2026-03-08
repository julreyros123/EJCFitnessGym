using System.ComponentModel.DataAnnotations;

namespace EJCFitnessGym.Models.Billing
{
    public class SubscriptionPlan
    {
        public int Id { get; set; }

        [Display(Name = "Plan tier")]
        public PlanTier Tier { get; set; } = PlanTier.Basic;

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        [Range(0, 999999)]
        public decimal Price { get; set; }

        public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;

        public bool IsActive { get; set; } = true;

        [Display(Name = "All branch access")]
        public bool AllowsAllBranchAccess { get; set; } = true;

        [Display(Name = "Basic equipment")]
        public bool IncludesBasicEquipment { get; set; } = true;

        [Display(Name = "Cardio access")]
        public bool IncludesCardioAccess { get; set; }

        [Display(Name = "Group classes")]
        public bool IncludesGroupClasses { get; set; }

        [Display(Name = "Free towel")]
        public bool IncludesFreeTowel { get; set; }

        [Display(Name = "Personal trainer")]
        public bool IncludesPersonalTrainer { get; set; }

        [Display(Name = "Fitness plan")]
        public bool IncludesFitnessPlan { get; set; }

        [Display(Name = "Full facility access")]
        public bool IncludesFullFacilityAccess { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
