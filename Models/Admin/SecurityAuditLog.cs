using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EJCFitnessGym.Models.Admin
{
    public class SecurityAuditLog
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(450)]
        public string? UserId { get; set; }

        [MaxLength(256)]
        public string? Email { get; set; }

        [Required]
        [MaxLength(100)]
        public string EventType { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string EventStatus { get; set; } = string.Empty;

        public string? EventDetails { get; set; }

        [MaxLength(45)]
        public string? IpAddress { get; set; }

        [MaxLength(500)]
        public string? UserAgent { get; set; }

        public DateTime EventTimestampUtc { get; set; } = DateTime.UtcNow;
    }
}
