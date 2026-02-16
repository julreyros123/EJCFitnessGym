using System.ComponentModel.DataAnnotations;

namespace EJCFitnessGym.Models.Finance
{
    public enum FinanceAlertState
    {
        New = 1,
        Acknowledged = 2,
        Resolved = 3,
        FalsePositive = 4
    }

    public class FinanceAlertLog
    {
        public int Id { get; set; }

        [Required]
        [StringLength(80)]
        public string AlertType { get; set; } = string.Empty;

        [StringLength(80)]
        public string? Trigger { get; set; }

        [StringLength(20)]
        public string Severity { get; set; } = "Medium";

        [Required]
        [StringLength(500)]
        public string Message { get; set; } = string.Empty;

        public bool RealtimePublished { get; set; }

        public bool EmailAttempted { get; set; }

        public bool EmailSucceeded { get; set; }

        [StringLength(4000)]
        public string? PayloadJson { get; set; }

        public FinanceAlertState State { get; set; } = FinanceAlertState.New;

        public DateTime? StateUpdatedUtc { get; set; }

        public DateTime? AcknowledgedUtc { get; set; }

        [StringLength(120)]
        public string? AcknowledgedBy { get; set; }

        public DateTime? ResolvedUtc { get; set; }

        [StringLength(120)]
        public string? ResolvedBy { get; set; }

        [StringLength(500)]
        public string? ResolutionNote { get; set; }

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }
}
