using EJCFitnessGym.Models.Billing;

namespace EJCFitnessGym.Services.Memberships
{
    public static class SubscriptionPlanCatalog
    {
        public static readonly IReadOnlyList<SubscriptionPlanPreset> DefaultPresets =
        [
            new(
                PlanTier.Basic,
                "Basic",
                "Train consistently across every Fitness Gym branch with essential gym-floor access.",
                999m,
                AllowsAllBranchAccess: true,
                IncludesBasicEquipment: true,
                IncludesCardioAccess: false,
                IncludesGroupClasses: false,
                IncludesFreeTowel: false,
                IncludesPersonalTrainer: false,
                IncludesFitnessPlan: false,
                IncludesFullFacilityAccess: false),
            new(
                PlanTier.Pro,
                "Pro",
                "Expand into cardio and guided sessions with added comfort perks across all branches.",
                1499m,
                AllowsAllBranchAccess: true,
                IncludesBasicEquipment: true,
                IncludesCardioAccess: true,
                IncludesGroupClasses: true,
                IncludesFreeTowel: true,
                IncludesPersonalTrainer: false,
                IncludesFitnessPlan: false,
                IncludesFullFacilityAccess: false),
            new(
                PlanTier.Elite,
                "Elite",
                "Unlock full branch access, coaching support, and premium recovery benefits.",
                1999m,
                AllowsAllBranchAccess: true,
                IncludesBasicEquipment: true,
                IncludesCardioAccess: true,
                IncludesGroupClasses: true,
                IncludesFreeTowel: true,
                IncludesPersonalTrainer: true,
                IncludesFitnessPlan: true,
                IncludesFullFacilityAccess: true)
        ];

        public static SubscriptionPlanPreset ResolvePreset(SubscriptionPlan plan)
        {
            var inferredTier = InferTier(plan);
            return DefaultPresets.First(preset => preset.Tier == inferredTier);
        }

        public static IReadOnlyList<string> BuildBenefits(SubscriptionPlan plan)
        {
            var preset = ResolvePreset(plan);
            var benefits = new List<string>();

            if (plan.AllowsAllBranchAccess || preset.AllowsAllBranchAccess)
            {
                benefits.Add("Access to all Fitness Gym branches");
            }

            if (plan.IncludesFullFacilityAccess || preset.IncludesFullFacilityAccess)
            {
                benefits.Add("Full access to all equipment and premium zones");
            }
            else
            {
                benefits.Add("Basic gym-floor equipment access");
            }

            if (plan.IncludesCardioAccess || preset.IncludesCardioAccess)
            {
                benefits.Add("Cardio training access");
            }

            if (plan.IncludesGroupClasses || preset.IncludesGroupClasses)
            {
                benefits.Add("Group exercise and specialty class access");
            }

            if (plan.IncludesFreeTowel || preset.IncludesFreeTowel)
            {
                benefits.Add("Free towel service");
            }

            if (plan.IncludesPersonalTrainer || preset.IncludesPersonalTrainer)
            {
                benefits.Add("Personal trainer support");
            }

            if (plan.IncludesFitnessPlan || preset.IncludesFitnessPlan)
            {
                benefits.Add("Personalized fitness plan");
            }

            return benefits;
        }

        public static string BuildAccessSummary(SubscriptionPlan plan)
        {
            var benefits = BuildBenefits(plan);
            return string.Join(" • ", benefits.Take(3));
        }

        public static string BuildSubtitle(SubscriptionPlan plan)
        {
            return string.IsNullOrWhiteSpace(plan.Description)
                ? ResolvePreset(plan).Description
                : plan.Description!;
        }

        public static SubscriptionPlanPreset? FindPresetByName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return DefaultPresets.FirstOrDefault(preset =>
                string.Equals(preset.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public static PlanTier InferTier(SubscriptionPlan plan)
        {
            if (plan.IncludesFullFacilityAccess || plan.IncludesPersonalTrainer || plan.IncludesFitnessPlan)
            {
                return PlanTier.Elite;
            }

            if (plan.IncludesCardioAccess || plan.IncludesGroupClasses || plan.IncludesFreeTowel)
            {
                return PlanTier.Pro;
            }

            if (string.IsNullOrWhiteSpace(plan.Name))
            {
                return plan.Tier;
            }

            if (plan.Name.Contains("Elite", StringComparison.OrdinalIgnoreCase))
            {
                return PlanTier.Elite;
            }

            if (plan.Name.Contains("Pro", StringComparison.OrdinalIgnoreCase))
            {
                return PlanTier.Pro;
            }

            if (plan.Name.Contains("Starter", StringComparison.OrdinalIgnoreCase) ||
                plan.Name.Contains("Basic", StringComparison.OrdinalIgnoreCase))
            {
                return PlanTier.Basic;
            }

            return plan.Tier;
        }

        public static SubscriptionPlan CreateDefaultPlan(SubscriptionPlanPreset preset)
        {
            return new SubscriptionPlan
            {
                Tier = preset.Tier,
                Name = preset.Name,
                Description = preset.Description,
                Price = preset.Price,
                BillingCycle = BillingCycle.Monthly,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow,
                AllowsAllBranchAccess = preset.AllowsAllBranchAccess,
                IncludesBasicEquipment = preset.IncludesBasicEquipment,
                IncludesCardioAccess = preset.IncludesCardioAccess,
                IncludesGroupClasses = preset.IncludesGroupClasses,
                IncludesFreeTowel = preset.IncludesFreeTowel,
                IncludesPersonalTrainer = preset.IncludesPersonalTrainer,
                IncludesFitnessPlan = preset.IncludesFitnessPlan,
                IncludesFullFacilityAccess = preset.IncludesFullFacilityAccess
            };
        }
    }

    public sealed record SubscriptionPlanPreset(
        PlanTier Tier,
        string Name,
        string Description,
        decimal Price,
        bool AllowsAllBranchAccess,
        bool IncludesBasicEquipment,
        bool IncludesCardioAccess,
        bool IncludesGroupClasses,
        bool IncludesFreeTowel,
        bool IncludesPersonalTrainer,
        bool IncludesFitnessPlan,
        bool IncludesFullFacilityAccess);
}
