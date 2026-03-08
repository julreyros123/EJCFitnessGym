using EJCFitnessGym.Controllers;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Tests;

public class SubscriptionPlansControllerTests
{
    [Fact]
    public async Task DeleteConfirmed_WithAssignments_DeactivatesPlan()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(DeleteConfirmed_WithAssignments_DeactivatesPlan));
        var db = dbHandle.Db;

        db.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Id = 1,
            Tier = PlanTier.Basic,
            Name = "Basic",
            Price = 999m,
            BillingCycle = BillingCycle.Monthly,
            IsActive = true
        });

        db.MemberSubscriptions.Add(new MemberSubscription
        {
            MemberUserId = "member-1",
            SubscriptionPlanId = 1,
            StartDateUtc = DateTime.UtcNow,
            Status = SubscriptionStatus.Active
        });

        await db.SaveChangesAsync();

        var controller = new SubscriptionPlansController(db)
        {
            TempData = BuildTempData()
        };

        var result = await controller.DeleteConfirmed(1);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(SubscriptionPlansController.Index), redirect.ActionName);

        var plan = await db.SubscriptionPlans.SingleAsync(p => p.Id == 1);
        Assert.False(plan.IsActive);
        Assert.Equal(
            "Plan has active history and was deactivated instead of deleted.",
            controller.TempData["StatusMessage"]);
    }

    [Fact]
    public async Task DeleteConfirmed_WithoutAssignments_RemovesPlan()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(DeleteConfirmed_WithoutAssignments_RemovesPlan));
        var db = dbHandle.Db;

        db.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Id = 2,
            Name = "Pro",
            Price = 1499m,
            BillingCycle = BillingCycle.Monthly,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var controller = new SubscriptionPlansController(db)
        {
            TempData = BuildTempData()
        };

        var result = await controller.DeleteConfirmed(2);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(SubscriptionPlansController.Index), redirect.ActionName);

        Assert.False(await db.SubscriptionPlans.AnyAsync(p => p.Id == 2));
    }

    [Fact]
    public async Task SeedDefaults_WhenNoPlansExist_AddsDefaultMonthlyPlans()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(SeedDefaults_WhenNoPlansExist_AddsDefaultMonthlyPlans));
        var db = dbHandle.Db;

        var controller = new SubscriptionPlansController(db)
        {
            TempData = BuildTempData()
        };

        var result = await controller.SeedDefaults();
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(SubscriptionPlansController.Index), redirect.ActionName);

        var plans = await db.SubscriptionPlans
            .OrderBy(p => p.Name)
            .ToListAsync();

        Assert.Equal(3, plans.Count);
        Assert.All(plans, plan =>
        {
            Assert.Equal(BillingCycle.Monthly, plan.BillingCycle);
            Assert.True(plan.IsActive);
            Assert.True(plan.Price > 0);
        });
        Assert.Equal(
            "Seeded 3 default subscription plan(s).",
            controller.TempData["StatusMessage"]);
    }

    [Fact]
    public async Task SeedDefaults_WhenDefaultsExist_DoesNotDuplicatePlans()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(SeedDefaults_WhenDefaultsExist_DoesNotDuplicatePlans));
        var db = dbHandle.Db;

        db.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Tier = PlanTier.Basic,
            Name = "Basic",
            Description = "Existing basic plan",
            Price = 1099m,
            BillingCycle = BillingCycle.Monthly,
            IsActive = true
        });
        db.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Tier = PlanTier.Pro,
            Name = "Pro",
            Description = "Existing pro plan",
            Price = 1599m,
            BillingCycle = BillingCycle.Monthly,
            IsActive = true
        });
        db.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Tier = PlanTier.Elite,
            Name = "Elite",
            Description = "Existing elite plan",
            Price = 2099m,
            BillingCycle = BillingCycle.Monthly,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var controller = new SubscriptionPlansController(db)
        {
            TempData = BuildTempData()
        };

        var result = await controller.SeedDefaults();
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(SubscriptionPlansController.Index), redirect.ActionName);

        var plans = await db.SubscriptionPlans.ToListAsync();
        Assert.Equal(3, plans.Count);
        Assert.Equal(
            "Default plans already exist. No new plans were added.",
            controller.TempData["StatusMessage"]);
    }

    [Fact]
    public async Task Edit_PreservesCreatedAtUtc_WhenUpdatingPlan()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(Edit_PreservesCreatedAtUtc_WhenUpdatingPlan));
        var db = dbHandle.Db;
        var createdAtUtc = DateTime.UtcNow.AddDays(-5);

        db.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Id = 9,
            Tier = PlanTier.Basic,
            Name = "Basic",
            Description = "Old description",
            Price = 999m,
            BillingCycle = BillingCycle.Monthly,
            IsActive = true,
            CreatedAtUtc = createdAtUtc
        });
        await db.SaveChangesAsync();

        var controller = new SubscriptionPlansController(db);
        var result = await controller.Edit(9, new SubscriptionPlan
        {
            Id = 9,
            Tier = PlanTier.Pro,
            Description = "Updated description",
            Price = 1299m,
            BillingCycle = BillingCycle.Monthly,
            IsActive = false
        });

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(nameof(SubscriptionPlansController.Index), redirect.ActionName);

        var updated = await db.SubscriptionPlans.SingleAsync(p => p.Id == 9);
        Assert.Equal("Pro", updated.Name);
        Assert.Equal("Expand into cardio and guided sessions with added comfort perks across all branches.", updated.Description);
        Assert.Equal(1299m, updated.Price);
        Assert.False(updated.IsActive);
        Assert.Equal(PlanTier.Pro, updated.Tier);
        Assert.True(updated.AllowsAllBranchAccess);
        Assert.True(updated.IncludesFreeTowel);
        Assert.Equal(createdAtUtc, updated.CreatedAtUtc);
    }

    private static TempDataDictionary BuildTempData()
    {
        return new TempDataDictionary(
            new DefaultHttpContext(),
            new TestTempDataProvider());
    }

    private static Task<InMemoryDbHandle> CreateDbContextAsync(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"subscription-plans-controller-tests-{databaseName}-{Guid.NewGuid():N}")
            .EnableSensitiveDataLogging()
            .Options;

        var db = new ApplicationDbContext(options);
        return Task.FromResult(new InMemoryDbHandle(db));
    }

    private sealed class InMemoryDbHandle : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; }

        public InMemoryDbHandle(ApplicationDbContext db)
        {
            Db = db;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
        }
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context)
        {
            return new Dictionary<string, object>();
        }

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
