using System.ComponentModel.DataAnnotations;

namespace EJCFitnessGym.Models.Finance
{
    public class GeneralLedgerEntry
    {
        public int Id { get; set; }

        [StringLength(32)]
        public string? BranchId { get; set; }

        [Required]
        [StringLength(40)]
        public string EntryNumber { get; set; } = string.Empty;

        public DateTime EntryDateUtc { get; set; } = DateTime.UtcNow;

        [Required]
        [StringLength(200)]
        public string Description { get; set; } = string.Empty;

        [StringLength(40)]
        public string? SourceType { get; set; }

        [StringLength(64)]
        public string? SourceId { get; set; }

        [StringLength(450)]
        public string? CreatedByUserId { get; set; }

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public ICollection<GeneralLedgerLine> Lines { get; set; } = new List<GeneralLedgerLine>();
    }

    public class GeneralLedgerLine
    {
        public int Id { get; set; }

        public int EntryId { get; set; }

        public int AccountId { get; set; }

        [StringLength(200)]
        public string? Memo { get; set; }

        [Range(0, 99999999)]
        public decimal Debit { get; set; }

        [Range(0, 99999999)]
        public decimal Credit { get; set; }

        public GeneralLedgerEntry? Entry { get; set; }

        public GeneralLedgerAccount? Account { get; set; }
    }
}
