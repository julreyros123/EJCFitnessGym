using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Admin;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Services.AI
{
    public sealed class MemberAiInsightWriter : IMemberAiInsightWriter
    {
        private const string LowActivityLabel = "Low Activity";
        private const string LowActivityActionType = "LowActivityOutreach";

        private readonly ApplicationDbContext _db;

        public MemberAiInsightWriter(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<MemberAiInsightWriteSummary> PersistAsync(
            IReadOnlyList<MemberSegmentationInput> inputs,
            MemberSegmentationBatchResult segmentation,
            string? actorUserId,
            CancellationToken cancellationToken = default)
        {
            if (inputs.Count == 0 || segmentation.ResultsByMemberId.Count == 0)
            {
                return new MemberAiInsightWriteSummary();
            }

            var memberIds = inputs
                .Select(input => input.MemberUserId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (memberIds.Count == 0)
            {
                return new MemberAiInsightWriteSummary();
            }

            var nowUtc = DateTime.UtcNow;
            var todayUtc = nowUtc.Date;

            var latestSnapshots = await _db.MemberSegmentSnapshots
                .Where(snapshot => memberIds.Contains(snapshot.MemberUserId))
                .GroupBy(snapshot => snapshot.MemberUserId)
                .Select(group => group
                    .OrderByDescending(snapshot => snapshot.CapturedAtUtc)
                    .ThenByDescending(snapshot => snapshot.Id)
                    .First())
                .ToListAsync(cancellationToken);

            var latestByMemberId = latestSnapshots.ToDictionary(
                snapshot => snapshot.MemberUserId,
                snapshot => snapshot,
                StringComparer.Ordinal);

            var openRetentionActions = await _db.MemberRetentionActions
                .Where(action =>
                    memberIds.Contains(action.MemberUserId) &&
                    action.ActionType == LowActivityActionType &&
                    (action.Status == MemberRetentionActionStatus.Open ||
                     action.Status == MemberRetentionActionStatus.InProgress))
                .ToListAsync(cancellationToken);

            var openActionByMemberId = openRetentionActions
                .GroupBy(action => action.MemberUserId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(action => action.UpdatedUtc).First(),
                    StringComparer.Ordinal);

            var snapshotsInserted = 0;
            var actionsCreated = 0;
            var actionsAutoClosed = 0;

            foreach (var input in inputs)
            {
                if (!segmentation.ResultsByMemberId.TryGetValue(input.MemberUserId, out var result))
                {
                    continue;
                }

                var needsSnapshot = true;
                if (latestByMemberId.TryGetValue(input.MemberUserId, out var latest))
                {
                    var sameCluster = latest.ClusterId == (int)result.ClusterId
                        && string.Equals(latest.SegmentLabel, result.SegmentLabel, StringComparison.OrdinalIgnoreCase);
                    var capturedToday = latest.CapturedAtUtc.Date == todayUtc;
                    needsSnapshot = !(sameCluster && capturedToday);
                }

                if (needsSnapshot)
                {
                    _db.MemberSegmentSnapshots.Add(new MemberSegmentSnapshot
                    {
                        MemberUserId = input.MemberUserId,
                        ClusterId = (int)result.ClusterId,
                        SegmentLabel = result.SegmentLabel,
                        SegmentDescription = result.SegmentDescription,
                        TotalSpending = decimal.Round((decimal)input.TotalSpending, 2, MidpointRounding.AwayFromZero),
                        BillingActivityCount = Math.Max(0, (int)Math.Round(input.BillingActivityCount)),
                        MembershipMonths = decimal.Round((decimal)input.MembershipMonths, 2, MidpointRounding.AwayFromZero),
                        CapturedAtUtc = nowUtc,
                        CapturedByUserId = actorUserId
                    });
                    snapshotsInserted += 1;
                }

                var isLowActivity = string.Equals(result.SegmentLabel, LowActivityLabel, StringComparison.OrdinalIgnoreCase);
                openActionByMemberId.TryGetValue(input.MemberUserId, out var openAction);

                if (isLowActivity)
                {
                    if (openAction is null)
                    {
                        _db.MemberRetentionActions.Add(new MemberRetentionAction
                        {
                            MemberUserId = input.MemberUserId,
                            ActionType = LowActivityActionType,
                            Status = MemberRetentionActionStatus.Open,
                            SegmentLabel = result.SegmentLabel,
                            Reason = "AI clustering identified low activity and lower value trend.",
                            SuggestedOffer = "Retention outreach + short-term plan incentive.",
                            DueDateUtc = nowUtc.Date.AddDays(7),
                            CreatedUtc = nowUtc,
                            UpdatedUtc = nowUtc,
                            CreatedByUserId = actorUserId,
                            UpdatedByUserId = actorUserId
                        });

                        actionsCreated += 1;
                    }
                }
                else if (openAction is not null)
                {
                    openAction.Status = MemberRetentionActionStatus.Completed;
                    openAction.UpdatedUtc = nowUtc;
                    openAction.UpdatedByUserId = actorUserId;
                    openAction.Notes = "Auto-completed after AI segment moved out of Low Activity.";
                    actionsAutoClosed += 1;
                }
            }

            if (snapshotsInserted > 0 || actionsCreated > 0 || actionsAutoClosed > 0)
            {
                await _db.SaveChangesAsync(cancellationToken);
            }

            return new MemberAiInsightWriteSummary
            {
                SnapshotsInserted = snapshotsInserted,
                RetentionActionsCreated = actionsCreated,
                RetentionActionsAutoClosed = actionsAutoClosed
            };
        }
    }
}
