namespace EJCFitnessGym.Services.AI
{
    public sealed class MemberChurnRiskService : IMemberChurnRiskService
    {
        public MemberChurnRiskBatchResult PredictRisk(IReadOnlyList<MemberChurnRiskInput> members)
        {
            if (members.Count == 0)
            {
                return new MemberChurnRiskBatchResult();
            }

            var results = new Dictionary<string, MemberChurnRiskResult>(StringComparer.Ordinal);
            foreach (var member in members)
            {
                var result = ScoreMember(member);
                results[member.MemberUserId] = result;
            }

            var summary = results.Values
                .GroupBy(result => result.RiskLevel, StringComparer.OrdinalIgnoreCase)
                .Select(group => new MemberChurnRiskSummaryItem
                {
                    RiskLevel = group.Key,
                    MemberCount = group.Count()
                })
                .OrderBy(item => RiskLevelOrder(item.RiskLevel))
                .ToList();

            return new MemberChurnRiskBatchResult
            {
                ResultsByMemberId = results,
                LevelSummary = summary
            };
        }

        private static MemberChurnRiskResult ScoreMember(MemberChurnRiskInput input)
        {
            var score = 0;
            var reasons = new List<string>(capacity: 6);

            if (!input.DaysSinceLastSuccessfulPayment.HasValue)
            {
                score += 35;
                reasons.Add("No successful payment history");
            }
            else if (input.DaysSinceLastSuccessfulPayment.Value >= 90f)
            {
                score += 40;
                reasons.Add("No payment in last 90+ days");
            }
            else if (input.DaysSinceLastSuccessfulPayment.Value >= 60f)
            {
                score += 30;
                reasons.Add("No payment in last 60+ days");
            }
            else if (input.DaysSinceLastSuccessfulPayment.Value >= 30f)
            {
                score += 18;
                reasons.Add("No payment in last 30+ days");
            }
            else if (input.DaysSinceLastSuccessfulPayment.Value >= 14f)
            {
                score += 10;
                reasons.Add("Payment cadence slowing down");
            }

            if (input.OverdueInvoiceCount >= 3)
            {
                score += 25;
                reasons.Add("Multiple expired invoices");
            }
            else if (input.OverdueInvoiceCount == 2)
            {
                score += 18;
                reasons.Add("Two expired invoices");
            }
            else if (input.OverdueInvoiceCount == 1)
            {
                score += 10;
                reasons.Add("One expired invoice");
            }

            if (input.DaysUntilMembershipEnd.HasValue)
            {
                if (input.DaysUntilMembershipEnd.Value < 0f)
                {
                    score += 20;
                    reasons.Add("Membership already expired");
                }
                else if (input.DaysUntilMembershipEnd.Value <= 7f)
                {
                    score += 18;
                    reasons.Add("Membership ending within 7 days");
                }
                else if (input.DaysUntilMembershipEnd.Value <= 30f)
                {
                    score += 10;
                    reasons.Add("Membership ending within 30 days");
                }
            }

            if (!input.HasActiveMembership)
            {
                score += 10;
                reasons.Add("No active membership status");
            }

            if (input.MembershipMonths < 2f)
            {
                score += 8;
                reasons.Add("Early-stage member with low tenure");
            }

            if (input.BillingActivityCount < 2f)
            {
                score += 12;
                reasons.Add("Low billing activity volume");
            }

            if (input.TotalSpending < 1500f)
            {
                score += 8;
                reasons.Add("Low spending profile");
            }

            var cappedScore = Math.Clamp(score, 0, 100);
            var riskLevel = ResolveRiskLevel(cappedScore);
            if (reasons.Count == 0)
            {
                reasons.Add("Stable payment and engagement behavior");
            }

            return new MemberChurnRiskResult
            {
                MemberUserId = input.MemberUserId,
                RiskScore = cappedScore,
                RiskLevel = riskLevel,
                Reasons = reasons,
                ReasonSummary = string.Join("; ", reasons.Take(3))
            };
        }

        private static string ResolveRiskLevel(int riskScore)
        {
            if (riskScore >= 70)
            {
                return "High";
            }

            if (riskScore >= 40)
            {
                return "Medium";
            }

            return "Low";
        }

        private static int RiskLevelOrder(string level)
        {
            if (string.Equals(level, "High", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (string.Equals(level, "Medium", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            return 2;
        }
    }
}
