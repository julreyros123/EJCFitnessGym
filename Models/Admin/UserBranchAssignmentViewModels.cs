namespace EJCFitnessGym.Models.Admin
{
    public sealed class UserBranchAssignmentListViewModel
    {
        public bool IsSuperAdmin { get; init; }

        public string CurrentUserId { get; init; } = string.Empty;

        public IReadOnlyList<BranchDirectoryItemViewModel> Branches { get; init; } =
            Array.Empty<BranchDirectoryItemViewModel>();

        public IReadOnlyList<UserBranchAssignmentItemViewModel> Users { get; init; } =
            Array.Empty<UserBranchAssignmentItemViewModel>();
    }

    public sealed class BranchDirectoryItemViewModel
    {
        public string BranchId { get; init; } = string.Empty;

        public string LocationName { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public bool IsActive { get; init; }

        public int AssignedUserCount { get; init; }

        public DateTime CreatedUtc { get; init; }
    }

    public sealed class UserBranchAssignmentItemViewModel
    {
        public string UserId { get; init; } = string.Empty;

        public string Email { get; init; } = string.Empty;

        public string RolesSummary { get; init; } = string.Empty;

        public string? BranchId { get; init; }

        public string? BranchDisplayName { get; init; }

        public bool IsSuperAdmin { get; init; }
    }
}
