using System.ComponentModel.DataAnnotations;

namespace EJCFitnessGym.Models.Admin
{
    public enum MemberRetentionActionStatus
    {
        Open = 1,
        InProgress = 2,
        Completed = 3,
        Dismissed = 4
    }

    public sealed class MemberSegmentSnapshot
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(450)]
        public string MemberUserId { get; set; } = string.Empty;

        public int ClusterId { get; set; }

        [Required]
        [MaxLength(64)]
        public string SegmentLabel { get; set; } = string.Empty;

        [Required]
        [MaxLength(220)]
        public string SegmentDescription { get; set; } = string.Empty;

        public decimal TotalSpending { get; set; }

        public int BillingActivityCount { get; set; }

        public decimal MembershipMonths { get; set; }

        public DateTime CapturedAtUtc { get; set; }

        [MaxLength(450)]
        public string? CapturedByUserId { get; set; }
    }

    public sealed class MemberRetentionAction
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(450)]
        public string MemberUserId { get; set; } = string.Empty;

        [Required]
        [MaxLength(64)]
        public string ActionType { get; set; } = string.Empty;

        public MemberRetentionActionStatus Status { get; set; } = MemberRetentionActionStatus.Open;

        [Required]
        [MaxLength(64)]
        public string SegmentLabel { get; set; } = string.Empty;

        [Required]
        [MaxLength(300)]
        public string Reason { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? SuggestedOffer { get; set; }

        public DateTime? DueDateUtc { get; set; }

        public DateTime CreatedUtc { get; set; }

        public DateTime UpdatedUtc { get; set; }

        [MaxLength(450)]
        public string? CreatedByUserId { get; set; }

        [MaxLength(450)]
        public string? UpdatedByUserId { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }
    }
}
