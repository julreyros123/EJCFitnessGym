using EJCFitnessGym.Models.Billing;

namespace EJCFitnessGym.Models.Admin
{
    public sealed class SubscriptionPlanListItemViewModel
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public string? Description { get; init; }

        public decimal Price { get; init; }

        public BillingCycle BillingCycle { get; init; }

        public bool IsActive { get; init; }

        public int TotalAssignments { get; init; }

        public int ActiveAssignments { get; init; }
    }
}
