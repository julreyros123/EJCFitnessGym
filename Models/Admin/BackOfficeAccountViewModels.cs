using System.ComponentModel.DataAnnotations;

namespace EJCFitnessGym.Models.Admin
{
    public sealed class BackOfficeAccountIndexViewModel
    {
        public BackOfficeAccountCreateInputViewModel CreateInput { get; init; } = new();

        public IReadOnlyList<BackOfficeRoleOptionViewModel> RoleOptions { get; init; } =
            Array.Empty<BackOfficeRoleOptionViewModel>();

        public IReadOnlyList<BackOfficeBranchOptionViewModel> BranchOptions { get; init; } =
            Array.Empty<BackOfficeBranchOptionViewModel>();

        public IReadOnlyList<BackOfficeAccountListItemViewModel> Accounts { get; init; } =
            Array.Empty<BackOfficeAccountListItemViewModel>();
    }

    public sealed class BackOfficeAccountCreateInputViewModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Phone]
        [Display(Name = "Phone Number")]
        public string? PhoneNumber { get; set; }

        [Required]
        [Display(Name = "Role")]
        public string Role { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Branch")]
        public string BranchId { get; set; } = string.Empty;

        [Required]
        [MinLength(8)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [Compare(nameof(Password), ErrorMessage = "Password confirmation does not match.")]
        [Display(Name = "Confirm Password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public sealed class BackOfficeRoleOptionViewModel
    {
        public string Value { get; init; } = string.Empty;

        public string Label { get; init; } = string.Empty;
    }

    public sealed class BackOfficeBranchOptionViewModel
    {
        public string BranchId { get; init; } = string.Empty;

        public string BranchName { get; init; } = string.Empty;
    }

    public sealed class BackOfficeAccountListItemViewModel
    {
        public string UserId { get; init; } = string.Empty;

        public string Email { get; init; } = string.Empty;

        public string? PhoneNumber { get; init; }

        public string RolesSummary { get; init; } = string.Empty;

        public string? BranchId { get; init; }

        public string? BranchName { get; init; }

        public DateTime? CreatedUtc { get; init; }

        public string? CreatedByUserId { get; init; }

        public string? CreatedByDisplay { get; init; }

        public bool IsActive { get; init; }

        public DateTime? StatusChangedUtc { get; init; }

        public string? StatusChangedByUserId { get; init; }

        public string? StatusChangedByDisplay { get; init; }

        public bool HasAuditRecord { get; init; }
    }
}
