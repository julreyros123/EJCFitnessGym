using System.ComponentModel.DataAnnotations;
using EJCFitnessGym.Models.Billing;

namespace EJCFitnessGym.Models.Admin
{
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
    }

    public class MemberAccountDeleteViewModel
    {
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PlanName { get; set; } = "No Plan";
    }
}
