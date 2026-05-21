using System;
using System.ComponentModel.DataAnnotations;

namespace EJCFitnessGym.Models.Admin
{
    public class SystemErrorLog
    {
        [Key]
        public int Id { get; set; }
        
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        
        public string? Path { get; set; }
        
        public string? ExceptionMessage { get; set; }
        
        public string? StackTrace { get; set; }
        
        [MaxLength(450)]
        public string? UserId { get; set; }
    }
}
