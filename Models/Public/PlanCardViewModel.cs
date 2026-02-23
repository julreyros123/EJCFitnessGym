namespace EJCFitnessGym.Models.Public
{
    public sealed class PlanCardViewModel
    {
        public int PlanId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public IReadOnlyList<string> Benefits { get; set; } = Array.Empty<string>();
        public bool IsFeatured { get; set; }
        public string? Badge { get; set; }
    }
}
