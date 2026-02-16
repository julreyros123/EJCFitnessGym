using System.ComponentModel.DataAnnotations;

namespace EJCFitnessGym.Models.Integration
{
    public class IntegrationOutboxMessage
    {
        public int Id { get; set; }

        public IntegrationOutboxTarget Target { get; set; } = IntegrationOutboxTarget.BackOffice;

        [Required]
        [MaxLength(120)]
        public string EventType { get; set; } = string.Empty;

        [Required]
        [MaxLength(300)]
        public string Message { get; set; } = string.Empty;

        [MaxLength(450)]
        public string? TargetValue { get; set; }

        public string? PayloadJson { get; set; }

        public IntegrationOutboxStatus Status { get; set; } = IntegrationOutboxStatus.Pending;

        public int AttemptCount { get; set; }

        [MaxLength(2000)]
        public string? LastError { get; set; }

        public DateTime NextAttemptUtc { get; set; } = DateTime.UtcNow;

        public DateTime? LastAttemptUtc { get; set; }

        public DateTime? ProcessedUtc { get; set; }

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }

    public enum IntegrationOutboxTarget
    {
        BackOffice = 1,
        Role = 2,
        User = 3
    }

    public enum IntegrationOutboxStatus
    {
        Pending = 1,
        Processing = 2,
        Processed = 3,
        Failed = 4
    }
}
