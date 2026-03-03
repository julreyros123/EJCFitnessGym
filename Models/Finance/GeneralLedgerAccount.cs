using System.ComponentModel.DataAnnotations;

namespace EJCFitnessGym.Models.Finance
{
    public enum GeneralLedgerAccountType
    {
        Asset = 1,
        Liability = 2,
        Equity = 3,
        Revenue = 4,
        Expense = 5
    }

    public class GeneralLedgerAccount
    {
        public int Id { get; set; }

        [StringLength(32)]
        public string? BranchId { get; set; }

        [Required]
        [StringLength(20)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [StringLength(120)]
        public string Name { get; set; } = string.Empty;

        public GeneralLedgerAccountType AccountType { get; set; } = GeneralLedgerAccountType.Asset;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        public ICollection<GeneralLedgerLine> Lines { get; set; } = new List<GeneralLedgerLine>();
    }
}
