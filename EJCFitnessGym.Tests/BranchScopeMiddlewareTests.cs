using System.Security.Claims;
using EJCFitnessGym.Security;
using Microsoft.AspNetCore.Http;

namespace EJCFitnessGym.Tests;

public class BranchScopeMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_BackOfficeWithoutBranchClaim_OnProtectedPath_ReturnsForbidden()
    {
        var nextCalled = false;
        var middleware = new BranchScopeMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext
        {
            User = BuildUser(role: "Admin", branchId: null),
        };
        context.Request.Path = "/Admin/Dashboard";

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_BackOfficeWithoutBranchClaim_OnInvoicesPath_ReturnsForbidden()
    {
        var nextCalled = false;
        var middleware = new BranchScopeMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext
        {
            User = BuildUser(role: "Finance", branchId: null),
        };
        context.Request.Path = "/Invoices";

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_BackOfficeWithoutBranchClaim_OnSubscriptionPlansPath_ReturnsForbidden()
    {
        var nextCalled = false;
        var middleware = new BranchScopeMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext
        {
            User = BuildUser(role: "Admin", branchId: null),
        };
        context.Request.Path = "/SubscriptionPlans";

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_BackOfficeWithoutBranchClaim_OnUserBranchesPath_AllowsRequest()
    {
        var nextCalled = false;
        var middleware = new BranchScopeMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext
        {
            User = BuildUser(role: "Admin", branchId: null),
        };
        context.Request.Path = "/Admin/UserBranches";

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task InvokeAsync_SuperAdminWithoutBranchClaim_OnProtectedPath_AllowsRequest()
    {
        var nextCalled = false;
        var middleware = new BranchScopeMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext
        {
            User = BuildUser(role: "SuperAdmin", branchId: null),
        };
        context.Request.Path = "/Admin/Dashboard";

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    private static ClaimsPrincipal BuildUser(string role, string? branchId)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, role) };
        if (!string.IsNullOrWhiteSpace(branchId))
        {
            claims.Add(new Claim(BranchAccess.BranchIdClaimType, branchId));
        }

        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }
}
