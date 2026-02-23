namespace EJCFitnessGym.Models.Admin
{
    public sealed class BranchRecord
    {
        public int Id { get; set; }

        public string BranchId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedUtc { get; set; }

        public DateTime UpdatedUtc { get; set; }

        public string? CreatedByUserId { get; set; }
    }
}
