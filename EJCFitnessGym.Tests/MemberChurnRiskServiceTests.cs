using EJCFitnessGym.Services.AI;

namespace EJCFitnessGym.Tests;

public class MemberChurnRiskServiceTests
{
    [Fact]
    public void PredictRisk_MissingPaymentsAndOverdues_ReturnsHighRisk()
    {
        var service = new MemberChurnRiskService();
        var input = new[]
        {
            new MemberChurnRiskInput
            {
                MemberUserId = "member-1",
                DisplayName = "member-1",
                TotalSpending = 0f,
                BillingActivityCount = 0f,
                MembershipMonths = 1f,
                DaysSinceLastSuccessfulPayment = null,
                DaysUntilMembershipEnd = -3f,
                OverdueInvoiceCount = 2,
                HasActiveMembership = false
            }
        };

        var result = service.PredictRisk(input);
        var memberResult = Assert.Single(result.ResultsByMemberId.Values);

        Assert.Equal("High", memberResult.RiskLevel);
        Assert.True(memberResult.RiskScore >= 70);
        Assert.Contains(memberResult.Reasons, reason => reason.Contains("No successful payment history", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PredictRisk_StableMember_ReturnsLowRisk()
    {
        var service = new MemberChurnRiskService();
        var input = new[]
        {
            new MemberChurnRiskInput
            {
                MemberUserId = "member-2",
                DisplayName = "member-2",
                TotalSpending = 12000f,
                BillingActivityCount = 8f,
                MembershipMonths = 14f,
                DaysSinceLastSuccessfulPayment = 4f,
                DaysUntilMembershipEnd = 120f,
                OverdueInvoiceCount = 0,
                HasActiveMembership = true
            }
        };

        var result = service.PredictRisk(input);
        var memberResult = Assert.Single(result.ResultsByMemberId.Values);

        Assert.Equal("Low", memberResult.RiskLevel);
        Assert.InRange(memberResult.RiskScore, 0, 39);
    }

    [Fact]
    public void PredictRisk_MixedMembers_BuildsLevelSummary()
    {
        var service = new MemberChurnRiskService();
        var input = new[]
        {
            new MemberChurnRiskInput
            {
                MemberUserId = "member-high",
                DisplayName = "member-high",
                TotalSpending = 500f,
                BillingActivityCount = 1f,
                MembershipMonths = 1f,
                DaysSinceLastSuccessfulPayment = 100f,
                DaysUntilMembershipEnd = 3f,
                OverdueInvoiceCount = 3,
                HasActiveMembership = false
            },
            new MemberChurnRiskInput
            {
                MemberUserId = "member-medium",
                DisplayName = "member-medium",
                TotalSpending = 3000f,
                BillingActivityCount = 2f,
                MembershipMonths = 4f,
                DaysSinceLastSuccessfulPayment = 40f,
                DaysUntilMembershipEnd = 20f,
                OverdueInvoiceCount = 2,
                HasActiveMembership = true
            },
            new MemberChurnRiskInput
            {
                MemberUserId = "member-low",
                DisplayName = "member-low",
                TotalSpending = 15000f,
                BillingActivityCount = 9f,
                MembershipMonths = 20f,
                DaysSinceLastSuccessfulPayment = 2f,
                DaysUntilMembershipEnd = 150f,
                OverdueInvoiceCount = 0,
                HasActiveMembership = true
            }
        };

        var result = service.PredictRisk(input);

        Assert.Equal(3, result.ResultsByMemberId.Count);
        Assert.Contains(result.LevelSummary, item => item.RiskLevel == "High" && item.MemberCount == 1);
        Assert.Contains(result.LevelSummary, item => item.RiskLevel == "Medium" && item.MemberCount == 1);
        Assert.Contains(result.LevelSummary, item => item.RiskLevel == "Low" && item.MemberCount == 1);
    }
}
