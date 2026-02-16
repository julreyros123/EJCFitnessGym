using System.ComponentModel.DataAnnotations;

namespace EJCFitnessGym.Models.Finance
{
    public class FinanceExpenseRecord
    {
        public int Id { get; set; }

        [Required]
        [StringLength(140)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [StringLength(80)]
        public string Category { get; set; } = string.Empty;

        [Range(0, 99999999)]
        public decimal Amount { get; set; }

        public DateTime ExpenseDateUtc { get; set; } = DateTime.UtcNow;

        public bool IsRecurring { get; set; }

        public bool IsActive { get; set; } = true;

        [StringLength(500)]
        public string? Notes { get; set; }

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}
