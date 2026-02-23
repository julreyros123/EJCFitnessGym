using System.ComponentModel.DataAnnotations;

namespace EJCFitnessGym.Models.Admin
{
    public sealed class StaffAccountIndexViewModel
    {
        public bool IsSuperAdmin { get; init; }

        public bool CanChooseBranch { get; init; }

        public string? DefaultBranchId { get; init; }

        public string? DefaultBranchName { get; init; }

        public StaffAccountCreateInputViewModel CreateInput { get; init; } = new();

        public IReadOnlyList<StaffBranchOptionViewModel> BranchOptions { get; init; } =
            Array.Empty<StaffBranchOptionViewModel>();

        public IReadOnlyList<StaffPositionOptionViewModel> PositionOptions { get; init; } =
            Array.Empty<StaffPositionOptionViewModel>();

        public IReadOnlyList<StaffAccountListItemViewModel> StaffAccounts { get; init; } =
            Array.Empty<StaffAccountListItemViewModel>();

        public IReadOnlyList<StaffAccountListItemViewModel> ActiveStaffAccounts { get; init; } =
            Array.Empty<StaffAccountListItemViewModel>();

        public IReadOnlyList<StaffAccountListItemViewModel> ArchivedStaffAccounts { get; init; } =
            Array.Empty<StaffAccountListItemViewModel>();
    }

    public sealed class StaffAccountCreateInputViewModel
    {
        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Phone]
        [Display(Name = "Phone Number")]
        public string? PhoneNumber { get; set; }

        [Required]
        [Display(Name = "Position")]
        public string Position { get; set; } = string.Empty;

        [Display(Name = "Branch")]
        public string? BranchId { get; set; }
    }

    public sealed class StaffBranchOptionViewModel
    {
        public string BranchId { get; init; } = string.Empty;

        public string BranchName { get; init; } = string.Empty;
    }

    public sealed class StaffPositionOptionViewModel
    {
        public string Value { get; init; } = string.Empty;

        public string Label { get; init; } = string.Empty;
    }

    public sealed class StaffAccountListItemViewModel
    {
        public string UserId { get; init; } = string.Empty;

        public string Email { get; init; } = string.Empty;

        public string? PhoneNumber { get; init; }

        public string? BranchId { get; init; }

        public string? BranchName { get; init; }

        public string? Position { get; init; }

        public bool IsArchived { get; init; }

        public string? ArchiveReason { get; init; }

        public DateTime? ArchivedAtUtc { get; init; }
    }
}
