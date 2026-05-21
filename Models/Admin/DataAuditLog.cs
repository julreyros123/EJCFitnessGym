using System;
using System.ComponentModel.DataAnnotations;

namespace EJCFitnessGym.Models.Admin
{
    public class DataAuditLog
    {
        [Key]
        public int Id { get; set; }

        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        [MaxLength(450)]
        public string? UserId { get; set; }

        [MaxLength(100)]
        public string EntityName { get; set; } = string.Empty;

        [MaxLength(50)]
        public string Action { get; set; } = string.Empty; 

        [MaxLength(256)]
        public string? PrimaryKey { get; set; }

        public string? OldValues { get; set; }

        public string? NewValues { get; set; }
    }
}
