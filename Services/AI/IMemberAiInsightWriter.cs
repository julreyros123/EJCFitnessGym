namespace EJCFitnessGym.Services.AI
{
    public sealed class MemberAiInsightWriteSummary
    {
        public int SnapshotsInserted { get; init; }

        public int RetentionActionsCreated { get; init; }

        public int RetentionActionsAutoClosed { get; init; }
    }

    public interface IMemberAiInsightWriter
    {
        Task<MemberAiInsightWriteSummary> PersistAsync(
            IReadOnlyList<MemberSegmentationInput> inputs,
            MemberSegmentationBatchResult segmentation,
            string? actorUserId,
            CancellationToken cancellationToken = default);
    }
}
