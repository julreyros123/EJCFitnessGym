namespace EJCFitnessGym.Services.AI
{
    public sealed class MemberSegmentationInput
    {
        public string MemberUserId { get; init; } = string.Empty;

        public string DisplayName { get; init; } = string.Empty;

        public float TotalSpending { get; init; }

        public float BillingActivityCount { get; init; }

        public float MembershipMonths { get; init; }
    }

    public sealed class MemberSegmentationResult
    {
        public string MemberUserId { get; init; } = string.Empty;

        public uint ClusterId { get; init; }

        public string SegmentLabel { get; init; } = string.Empty;

        public string SegmentDescription { get; init; } = string.Empty;

        public float[] Distances { get; init; } = Array.Empty<float>();
    }

    public sealed class MemberSegmentSummaryItem
    {
        public string SegmentLabel { get; init; } = string.Empty;

        public string SegmentDescription { get; init; } = string.Empty;

        public int MemberCount { get; init; }
    }

    public sealed class MemberSegmentationBatchResult
    {
        public IReadOnlyDictionary<string, MemberSegmentationResult> ResultsByMemberId { get; init; } =
            new Dictionary<string, MemberSegmentationResult>(StringComparer.Ordinal);

        public IReadOnlyList<MemberSegmentSummaryItem> SegmentSummary { get; init; } =
            Array.Empty<MemberSegmentSummaryItem>();
    }

    public interface IMemberSegmentationService
    {
        MemberSegmentationBatchResult SegmentMembers(
            IReadOnlyList<MemberSegmentationInput> members,
            int preferredClusterCount = 3);
    }
}
