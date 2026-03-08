using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Models.Public;

namespace EJCFitnessGym.Services.Memberships
{
    public static class PlanCardCatalogBuilder
    {
        public static List<PlanCardViewModel> Build(IReadOnlyList<SubscriptionPlan> plans)
        {
            var cards = new List<PlanCardViewModel>(plans.Count);

            for (var index = 0; index < plans.Count; index++)
            {
                var plan = plans[index];
                var preset = SubscriptionPlanCatalog.ResolvePreset(plan);
                var benefits = SubscriptionPlanCatalog.BuildBenefits(plan);
                var subtitle = SubscriptionPlanCatalog.BuildSubtitle(plan);

                cards.Add(new PlanCardViewModel
                {
                    PlanId = plan.Id,
                    Tier = preset.Tier,
                    Name = string.IsNullOrWhiteSpace(plan.Name) ? preset.Name : plan.Name,
                    Subtitle = subtitle,
                    Price = plan.Price,
                    Benefits = benefits,
                    IsFeatured = preset.Tier == PlanTier.Pro,
                    Badge = preset.Tier switch
                    {
                        PlanTier.Pro => "Most Popular",
                        PlanTier.Elite => "Full Access",
                        _ => null
                    }
                });
            }

            if (cards.Count > 0 && cards.All(card => !card.IsFeatured))
            {
                var featuredIndex = Math.Min(1, cards.Count - 1);
                cards[featuredIndex].IsFeatured = true;
                cards[featuredIndex].Badge ??= "Recommended";
            }

            return cards;
        }
    }
}
