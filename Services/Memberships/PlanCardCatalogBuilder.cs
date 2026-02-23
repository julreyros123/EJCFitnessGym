using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Models.Public;

namespace EJCFitnessGym.Services.Memberships
{
    public static class PlanCardCatalogBuilder
    {
        private static readonly string[] TierFallbackNames = { "Starter", "Pro", "Elite" };

        private static readonly Dictionary<string, PlanDisplayTemplate> TierTemplates =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Starter"] = new(
                    "For regular gym sessions and consistency goals.",
                    new[]
                    {
                        "Full gym access",
                        "Basic progress tracking",
                        "Support from front desk team",
                        "Cancel anytime"
                    },
                    false,
                    null
                ),
                ["Pro"] = new(
                    "For members targeting measurable weekly progression.",
                    new[]
                    {
                        "Everything in Starter",
                        "2 coach check-ins per month",
                        "Priority class booking",
                        "Cancel anytime"
                    },
                    true,
                    "Most Popular"
                ),
                ["Elite"] = new(
                    "For complete coaching support and faster results.",
                    new[]
                    {
                        "Everything in Pro",
                        "Weekly coach sessions",
                        "Nutrition consultations",
                        "Cancel anytime"
                    },
                    false,
                    "Best Value"
                )
            };

        public static List<PlanCardViewModel> Build(IReadOnlyList<SubscriptionPlan> plans)
        {
            var cards = new List<PlanCardViewModel>(plans.Count);

            for (var index = 0; index < plans.Count; index++)
            {
                var plan = plans[index];
                var displayName = ResolveDisplayName(plan.Name, index);
                var hasKnownTierTemplate = TierTemplates.TryGetValue(displayName, out var knownTemplate);

                var template = hasKnownTierTemplate && knownTemplate is not null
                    ? knownTemplate
                    : new PlanDisplayTemplate(
                        "Flexible monthly gym membership.",
                        new[]
                        {
                            "Full gym access",
                            "Member progress tracking",
                            "Cancel anytime"
                        },
                        false,
                        null
                    );

                if (!hasKnownTierTemplate && !string.IsNullOrWhiteSpace(plan.Description))
                {
                    template = template with { Subtitle = plan.Description };
                }

                cards.Add(new PlanCardViewModel
                {
                    PlanId = plan.Id,
                    Name = displayName,
                    Subtitle = template.Subtitle,
                    Price = plan.Price,
                    Benefits = template.Benefits,
                    IsFeatured = template.IsFeatured,
                    Badge = template.Badge
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

        private static string ResolveDisplayName(string planName, int index)
        {
            if (!string.IsNullOrWhiteSpace(planName))
            {
                var matchedTier = TierTemplates.Keys
                    .FirstOrDefault(tier => planName.Contains(tier, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(matchedTier))
                {
                    return matchedTier;
                }
            }

            if (index < TierFallbackNames.Length)
            {
                return TierFallbackNames[index];
            }

            return string.IsNullOrWhiteSpace(planName) ? $"Plan {index + 1}" : planName;
        }

        private sealed record PlanDisplayTemplate(
            string Subtitle,
            IReadOnlyList<string> Benefits,
            bool IsFeatured,
            string? Badge
        );
    }
}
