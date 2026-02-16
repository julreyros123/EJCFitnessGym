using System.ComponentModel.DataAnnotations;

namespace EJCFitnessGym.Models.Integration
{
    public class InboundWebhookReceipt
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(80)]
        public string Provider { get; set; } = string.Empty;

        [Required]
        [MaxLength(250)]
        public string EventKey { get; set; } = string.Empty;

        [MaxLength(120)]
        public string? EventType { get; set; }

        [MaxLength(180)]
        public string? ExternalReference { get; set; }

        [Required]
        [MaxLength(40)]
        public string Status { get; set; } = "Processing";

        public int AttemptCount { get; set; } = 1;

        public DateTime FirstReceivedUtc { get; set; } = DateTime.UtcNow;

        public DateTime LastAttemptUtc { get; set; } = DateTime.UtcNow;

        public DateTime? ProcessedUtc { get; set; }

        [MaxLength(2000)]
        public string? Notes { get; set; }

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }
}
