using System.ComponentModel.DataAnnotations;
using EJCFitnessGym.Models.Billing;

namespace EJCFitnessGym.Models.Admin
{
    public class MemberAccountIndexViewModel
    {
        public IReadOnlyList<MemberAccountListItemViewModel> Members { get; set; } =
            Array.Empty<MemberAccountListItemViewModel>();

        public IReadOnlyList<MemberAccountClusterSummaryItemViewModel> ClusterSummary { get; set; } =
            Array.Empty<MemberAccountClusterSummaryItemViewModel>();

        public IReadOnlyList<MemberAccountChurnSummaryItemViewModel> ChurnSummary { get; set; } =
            Array.Empty<MemberAccountChurnSummaryItemViewModel>();

        public DateTime SegmentedAtUtc { get; set; } = DateTime.UtcNow;
    }

    public class MemberAccountClusterSummaryItemViewModel
    {
        public string SegmentName { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public int MemberCount { get; set; }
    }

    public class MemberAccountChurnSummaryItemViewModel
    {
        public string RiskLevel { get; set; } = string.Empty;

        public int MemberCount { get; set; }
    }

    public class MemberAccountListItemViewModel
    {
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string PlanName { get; set; } = "No Plan";
        public SubscriptionStatus? SubscriptionStatus { get; set; }
        public DateTime? StartDateUtc { get; set; }
        public DateTime? EndDateUtc { get; set; }
        public bool IsMembershipActive { get; set; }
        public uint? AiClusterId { get; set; }
        public string? AiSegmentLabel { get; set; }
        public string? AiSegmentDescription { get; set; }
        public int? AiChurnRiskScore { get; set; }
        public string? AiChurnRiskLevel { get; set; }
        public string? AiChurnReasonSummary { get; set; }
        public bool HasOpenRetentionAction { get; set; }
        public string? RetentionActionStatus { get; set; }
        public DateTime? RetentionDueDateUtc { get; set; }
    }

    public class MemberAccountFormViewModel
    {
        public string? UserId { get; set; }

        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Display(Name = "Password")]
        [StringLength(100, MinimumLength = 6)]
        [DataType(DataType.Password)]
        public string? Password { get; set; }

        [Display(Name = "Confirm Password")]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Password and confirmation do not match.")]
        public string? ConfirmPassword { get; set; }

        [MaxLength(100)]
        public string? FirstName { get; set; }

        [MaxLength(100)]
        public string? LastName { get; set; }

        [Phone]
        [MaxLength(30)]
        public string? PhoneNumber { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = "Please select a membership plan.")]
        [Display(Name = "Membership Plan")]
        public int SubscriptionPlanId { get; set; }

        [Display(Name = "Start Date")]
        [DataType(DataType.Date)]
        public DateTime StartDateUtc { get; set; } = DateTime.UtcNow.Date;

        [Display(Name = "End Date")]
        [DataType(DataType.Date)]
        public DateTime? EndDateUtc { get; set; }

        [Display(Name = "Subscription Status")]
        public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;

        public bool RequirePassword { get; set; }
    }

    public class MemberAccountDetailsViewModel
    {
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string PlanName { get; set; } = "No Plan";
        public SubscriptionStatus? SubscriptionStatus { get; set; }
        public DateTime? StartDateUtc { get; set; }
        public DateTime? EndDateUtc { get; set; }
        public bool IsMembershipActive { get; set; }
        public uint? AiClusterId { get; set; }
        public string? AiSegmentLabel { get; set; }
        public string? AiSegmentDescription { get; set; }
        public DateTime? AiSegmentCapturedAtUtc { get; set; }
        public bool HasOpenRetentionAction { get; set; }
        public string? RetentionActionStatus { get; set; }
        public DateTime? RetentionDueDateUtc { get; set; }
        public string? RetentionReason { get; set; }
        public string? RetentionSuggestedOffer { get; set; }
    }

    public class MemberAccountDeleteViewModel
    {
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PlanName { get; set; } = "No Plan";
    }
}
