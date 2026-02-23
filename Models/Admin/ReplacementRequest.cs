using System.ComponentModel.DataAnnotations;

namespace EJCFitnessGym.Models.Admin
{
    public class ReplacementRequest
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(32)]
        public string RequestNumber { get; set; } = string.Empty;

        [Required]
        [MaxLength(32)]
        public string BranchId { get; set; } = string.Empty;

        [Required]
        [MaxLength(450)]
        public string RequestedByUserId { get; set; } = string.Empty;

        [Required]
        [MaxLength(160)]
        public string Subject { get; set; } = string.Empty;

        [Required]
        [MaxLength(2000)]
        public string Description { get; set; } = string.Empty;

        public ReplacementRequestType RequestType { get; set; } = ReplacementRequestType.Equipment;

        public ReplacementRequestPriority Priority { get; set; } = ReplacementRequestPriority.Medium;

        public ReplacementRequestStatus Status { get; set; } = ReplacementRequestStatus.Requested;

        [MaxLength(450)]
        public string? ReviewedByUserId { get; set; }

        [MaxLength(1000)]
        public string? AdminNotes { get; set; }

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        public DateTime? ResolvedUtc { get; set; }
    }

    public enum ReplacementRequestType
    {
        Equipment = 1,
        Supplies = 2,
        Facility = 3,
        MemberConcern = 4,
        Other = 5
    }

    public enum ReplacementRequestPriority
    {
        Low = 1,
        Medium = 2,
        High = 3,
        Urgent = 4
    }

    public enum ReplacementRequestStatus
    {
        Requested = 1,
        InReview = 2,
        Approved = 3,
        Rejected = 4,
        Completed = 5
    }
}
