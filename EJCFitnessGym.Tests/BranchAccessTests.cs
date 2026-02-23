using System.Security.Claims;
using EJCFitnessGym.Security;

namespace EJCFitnessGym.Tests;

public class BranchAccessTests
{
    [Fact]
    public void HasBranchScope_ReturnsTrue_ForSuperAdminWithoutBranchClaim()
    {
        var principal = BuildPrincipal(
            isAuthenticated: true,
            role: "SuperAdmin");

        Assert.True(principal.HasBranchScope());
    }

    [Fact]
    public void HasBranchScope_ReturnsTrue_ForBackOfficeUserWithBranchClaim()
    {
        var principal = BuildPrincipal(
            isAuthenticated: true,
            role: "Admin",
            branchId: "BR-CENTRAL");

        Assert.True(principal.HasBranchScope());
    }

    [Fact]
    public void HasBranchScope_ReturnsFalse_ForBackOfficeUserWithoutBranchClaim()
    {
        var principal = BuildPrincipal(
            isAuthenticated: true,
            role: "Finance");

        Assert.False(principal.HasBranchScope());
    }

    [Fact]
    public void GetBranchId_TrimsClaimValue()
    {
        var principal = BuildPrincipal(
            isAuthenticated: true,
            role: "Staff",
            branchId: "  BR-NORTH  ");

        Assert.Equal("BR-NORTH", principal.GetBranchId());
    }

    private static ClaimsPrincipal BuildPrincipal(bool isAuthenticated, string role, string? branchId = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, role) };
        if (!string.IsNullOrWhiteSpace(branchId))
        {
            claims.Add(new Claim(BranchAccess.BranchIdClaimType, branchId));
        }

        var authenticationType = isAuthenticated ? "TestAuth" : null;
        var identity = new ClaimsIdentity(claims, authenticationType);
        return new ClaimsPrincipal(identity);
    }
}
