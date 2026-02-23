using Microsoft.ML;
using Microsoft.ML.Data;

namespace EJCFitnessGym.Services.AI
{
    public sealed class MemberSegmentationService : IMemberSegmentationService
    {
        private static readonly MLContext MlContext = new(seed: 17);

        private readonly record struct SegmentProfile(string Label, string Description);

        private sealed class MemberObservation
        {
            public string MemberUserId { get; set; } = string.Empty;

            public float TotalSpending { get; set; }

            public float BillingActivityCount { get; set; }

            public float MembershipMonths { get; set; }
        }

        private sealed class MemberPrediction
        {
            [ColumnName("PredictedLabel")]
            public uint ClusterId { get; set; }

            public float[] Distances { get; set; } = Array.Empty<float>();
        }

        private sealed class ScoredMember
        {
            public string MemberUserId { get; init; } = string.Empty;

            public float TotalSpending { get; init; }

            public float BillingActivityCount { get; init; }

            public float MembershipMonths { get; init; }

            public uint ClusterId { get; init; }

            public float[] Distances { get; init; } = Array.Empty<float>();
        }

        public MemberSegmentationBatchResult SegmentMembers(
            IReadOnlyList<MemberSegmentationInput> members,
            int preferredClusterCount = 3)
        {
            if (members.Count == 0)
            {
                return new MemberSegmentationBatchResult();
            }

            var observations = members
                .Select(member => new MemberObservation
                {
                    MemberUserId = member.MemberUserId,
                    TotalSpending = member.TotalSpending,
                    BillingActivityCount = member.BillingActivityCount,
                    MembershipMonths = member.MembershipMonths
                })
                .ToList();

            if (observations.Count < 2 || HasUniformFeatures(observations))
            {
                return BuildUniformResult(observations);
            }

            var requestedClusters = Math.Clamp(preferredClusterCount, 2, observations.Count);
            var trainingData = MlContext.Data.LoadFromEnumerable(observations);

            var pipeline = MlContext.Transforms
                .Concatenate(
                    "Features",
                    nameof(MemberObservation.TotalSpending),
                    nameof(MemberObservation.BillingActivityCount),
                    nameof(MemberObservation.MembershipMonths))
                .Append(MlContext.Transforms.NormalizeMinMax("Features"))
                .Append(MlContext.Clustering.Trainers.KMeans(
                    featureColumnName: "Features",
                    numberOfClusters: requestedClusters));

            var model = pipeline.Fit(trainingData);
            var transformed = model.Transform(trainingData);
            var predictions = MlContext.Data
                .CreateEnumerable<MemberPrediction>(transformed, reuseRowObject: false)
                .ToList();

            var scoredMembers = observations
                .Select((observation, index) =>
                {
                    var prediction = predictions[index];
                    return new ScoredMember
                    {
                        MemberUserId = observation.MemberUserId,
                        TotalSpending = observation.TotalSpending,
                        BillingActivityCount = observation.BillingActivityCount,
                        MembershipMonths = observation.MembershipMonths,
                        ClusterId = prediction.ClusterId,
                        Distances = prediction.Distances
                    };
                })
                .ToList();

            var segmentProfilesByCluster = BuildSegmentProfiles(scoredMembers);

            var resultsByMemberId = scoredMembers.ToDictionary(
                item => item.MemberUserId,
                item =>
                {
                    var profile = segmentProfilesByCluster[item.ClusterId];
                    return new MemberSegmentationResult
                    {
                        MemberUserId = item.MemberUserId,
                        ClusterId = item.ClusterId,
                        SegmentLabel = profile.Label,
                        SegmentDescription = profile.Description,
                        Distances = item.Distances
                    };
                },
                StringComparer.Ordinal);

            var summary = resultsByMemberId.Values
                .GroupBy(result => result.SegmentLabel, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var sample = group.First();
                    return new MemberSegmentSummaryItem
                    {
                        SegmentLabel = sample.SegmentLabel,
                        SegmentDescription = sample.SegmentDescription,
                        MemberCount = group.Count()
                    };
                })
                .OrderByDescending(item => item.MemberCount)
                .ThenBy(item => item.SegmentLabel, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new MemberSegmentationBatchResult
            {
                ResultsByMemberId = resultsByMemberId,
                SegmentSummary = summary
            };
        }

        private static bool HasUniformFeatures(IReadOnlyList<MemberObservation> observations)
        {
            if (observations.Count <= 1)
            {
                return true;
            }

            var first = observations[0];
            return observations.All(item =>
                item.TotalSpending == first.TotalSpending &&
                item.BillingActivityCount == first.BillingActivityCount &&
                item.MembershipMonths == first.MembershipMonths);
        }

        private static MemberSegmentationBatchResult BuildUniformResult(IReadOnlyList<MemberObservation> observations)
        {
            var results = observations.ToDictionary(
                member => member.MemberUserId,
                member => new MemberSegmentationResult
                {
                    MemberUserId = member.MemberUserId,
                    ClusterId = 1,
                    SegmentLabel = "Regular Members",
                    SegmentDescription = "Typical engagement and spending pattern."
                },
                StringComparer.Ordinal);

            return new MemberSegmentationBatchResult
            {
                ResultsByMemberId = results,
                SegmentSummary = new[]
                {
                    new MemberSegmentSummaryItem
                    {
                        SegmentLabel = "Regular Members",
                        SegmentDescription = "Typical engagement and spending pattern.",
                        MemberCount = observations.Count
                    }
                }
            };
        }

        private static Dictionary<uint, SegmentProfile> BuildSegmentProfiles(
            IReadOnlyList<ScoredMember> scoredMembers)
        {
            var spendingRange = GetRange(scoredMembers.Select(item => item.TotalSpending));
            var activityRange = GetRange(scoredMembers.Select(item => item.BillingActivityCount));
            var tenureRange = GetRange(scoredMembers.Select(item => item.MembershipMonths));

            var clusterScores = scoredMembers
                .GroupBy(item => item.ClusterId)
                .Select(group =>
                {
                    var composite = group
                        .Select(item =>
                            Normalize(item.TotalSpending, spendingRange.min, spendingRange.max) * 0.5f +
                            Normalize(item.BillingActivityCount, activityRange.min, activityRange.max) * 0.3f +
                            Normalize(item.MembershipMonths, tenureRange.min, tenureRange.max) * 0.2f)
                        .DefaultIfEmpty(0f)
                        .Average();

                    return new
                    {
                        ClusterId = group.Key,
                        Composite = composite
                    };
                })
                .OrderBy(item => item.Composite)
                .ToList();

            var profiles = new Dictionary<uint, SegmentProfile>();
            for (var index = 0; index < clusterScores.Count; index += 1)
            {
                var cluster = clusterScores[index];
                profiles[cluster.ClusterId] = ResolveProfile(index, clusterScores.Count);
            }

            return profiles;
        }

        private static SegmentProfile ResolveProfile(int sortedIndex, int totalClusters)
        {
            if (totalClusters <= 1)
            {
                return new SegmentProfile(
                    "Regular Members",
                    "Typical engagement and spending pattern.");
            }

            if (sortedIndex == 0)
            {
                return new SegmentProfile(
                    "Low Activity",
                    "Lower billing activity and value; retention opportunity.");
            }

            if (sortedIndex == totalClusters - 1)
            {
                return new SegmentProfile(
                    "High Value",
                    "Strong spending and engagement; priority member segment.");
            }

            return new SegmentProfile(
                "Regular Members",
                "Stable engagement with balanced value.");
        }

        private static (float min, float max) GetRange(IEnumerable<float> values)
        {
            var list = values.ToList();
            if (list.Count == 0)
            {
                return (0f, 0f);
            }

            return (list.Min(), list.Max());
        }

        private static float Normalize(float value, float min, float max)
        {
            var denominator = max - min;
            if (denominator <= 0f)
            {
                return 0f;
            }

            return (value - min) / denominator;
        }
    }
}
