using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Models.Finance;
using EJCFitnessGym.Security;
using EJCFitnessGym.Services.Finance;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Tests;

public class FinanceMetricsServiceTests
{
    [Fact]
    public async Task GetOverviewAsync_ComputesRevenueCostsAndNetProfit()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(GetOverviewAsync_ComputesRevenueCostsAndNetProfit));
        var db = dbHandle.Db;
        var service = new FinanceMetricsService(db);

        var todayUtc = DateTime.UtcNow.Date;
        var fromUtc = todayUtc.AddDays(-10);
        var toUtc = todayUtc.AddDays(1).AddTicks(-1);

        await SeedPaymentAsync(db, amount: 2000m, status: PaymentStatus.Succeeded, paidAtUtc: todayUtc.AddDays(-1).AddHours(12), gatewayProvider: "PayMongo");
        await SeedPaymentAsync(db, amount: 1000m, status: PaymentStatus.Succeeded, paidAtUtc: todayUtc.AddDays(-2).AddHours(12), gatewayProvider: "Manual");
        await SeedPaymentAsync(db, amount: 900m, status: PaymentStatus.Failed, paidAtUtc: todayUtc.AddDays(-3).AddHours(12), gatewayProvider: "PayMongo");

        await SeedExpenseAsync(db, amount: 400m, expenseDateUtc: todayUtc.AddDays(-4).AddHours(8), category: "Utilities");
        await SeedExpenseAsync(db, amount: 999m, expenseDateUtc: todayUtc.AddDays(-40).AddHours(8), category: "OutOfRange");

        db.GymEquipmentAssets.Add(new GymEquipmentAsset
        {
            Name = "Treadmill A",
            Brand = "BrandX",
            Category = "Cardio",
            Quantity = 2,
            UnitCost = 60000m,
            UsefulLifeMonths = 60,
            PurchasedAtUtc = todayUtc.AddMonths(-3),
            IsActive = true,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var overview = await service.GetOverviewAsync(fromUtc, toUtc);

        Assert.Equal(2, overview.SuccessfulPaymentsCount);
        Assert.Equal(3000m, overview.TotalRevenue);
        Assert.Equal(2000m, overview.PayMongoRevenue);
        Assert.Equal(400m, overview.OperatingExpenses);
        Assert.Equal(2400m, overview.TotalCosts);
        Assert.Equal(1, overview.EquipmentAssetItemCount);
        Assert.Equal(2, overview.EquipmentTotalUnits);
        Assert.Equal(120000m, overview.EquipmentTotalInvestment);
        Assert.Equal(2000m, overview.EquipmentMonthlyDepreciation);
        Assert.Equal(600m, overview.EstimatedNetProfit);
        Assert.Equal(2.5m, overview.EquipmentPaybackPercent);
    }

    [Fact]
    public async Task GetInsightsAsync_ReturnsProjectedLoss_WhenForecastNetIsNegative()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(GetInsightsAsync_ReturnsProjectedLoss_WhenForecastNetIsNegative));
        var db = dbHandle.Db;
        var service = new FinanceMetricsService(db);

        var todayUtc = DateTime.UtcNow.Date;
        for (var i = 0; i < 30; i++)
        {
            await SeedExpenseAsync(
                db,
                amount: 1000m,
                expenseDateUtc: todayUtc.AddDays(-i).AddHours(9),
                category: "Operations");
        }

        db.GymEquipmentAssets.Add(new GymEquipmentAsset
        {
            Name = "Cable Machine",
            Brand = "BrandY",
            Category = "Strength Machine",
            Quantity = 1,
            UnitCost = 90000m,
            UsefulLifeMonths = 30,
            PurchasedAtUtc = todayUtc.AddMonths(-2),
            IsActive = true,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var insights = await service.GetInsightsAsync(lookbackDays: 30, forecastDays: 30);

        Assert.Equal(0m, insights.ForecastRevenue);
        Assert.True(insights.ForecastNet < 0m);
        Assert.Equal("Projected Loss", insights.GainOrLossSignal);
        Assert.Equal("High", insights.RiskLevel);
    }

    [Fact]
    public async Task GetInsightsAsync_DetectsRevenueSpikeAnomaly()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(GetInsightsAsync_DetectsRevenueSpikeAnomaly));
        var db = dbHandle.Db;
        var service = new FinanceMetricsService(db);

        var todayUtc = DateTime.UtcNow.Date;
        var spikeDay = todayUtc.AddDays(-3);

        for (var i = 0; i < 30; i++)
        {
            var day = todayUtc.AddDays(-i);
            var baseline = i % 3 switch
            {
                0 => 95m,
                1 => 100m,
                _ => 105m
            };

            var amount = day == spikeDay ? 5000m : baseline;
            await SeedPaymentAsync(
                db,
                amount: amount,
                status: PaymentStatus.Succeeded,
                paidAtUtc: day.AddHours(11),
                gatewayProvider: "PayMongo");
        }

        var insights = await service.GetInsightsAsync(lookbackDays: 30, forecastDays: 30);

        var revenueSpike = insights.Anomalies.FirstOrDefault(a => a.Type == "Revenue Spike");
        Assert.NotNull(revenueSpike);
        Assert.Equal(spikeDay, revenueSpike!.DateUtc.Date);
        Assert.Equal("High", revenueSpike.Severity);
        Assert.Equal("High", insights.RiskLevel);
    }

    [Fact]
    public async Task SeedMediumGymSampleAsync_IsIdempotent()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(SeedMediumGymSampleAsync_IsIdempotent));
        var db = dbHandle.Db;
        var service = new FinanceMetricsService(db);

        var first = await service.SeedMediumGymSampleAsync();
        var second = await service.SeedMediumGymSampleAsync();

        Assert.True(first.InsertedCount > 0);
        Assert.Equal(first.InsertedCount, first.TotalAssets);
        Assert.Equal(0, first.SkippedCount);

        Assert.Equal(0, second.InsertedCount);
        Assert.Equal(first.TotalAssets, second.TotalAssets);
        Assert.Equal(first.TotalAssets, second.SkippedCount);
    }

    [Fact]
    public async Task GetOverviewAsync_WithBranchId_FiltersRevenueToScopedMembers()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(GetOverviewAsync_WithBranchId_FiltersRevenueToScopedMembers));
        var db = dbHandle.Db;
        var service = new FinanceMetricsService(db);

        var nowUtc = DateTime.UtcNow;

        db.UserClaims.AddRange(
            new IdentityUserClaim<string>
            {
                UserId = "member-branch-a",
                ClaimType = BranchAccess.BranchIdClaimType,
                ClaimValue = "BR-A"
            },
            new IdentityUserClaim<string>
            {
                UserId = "member-branch-b",
                ClaimType = BranchAccess.BranchIdClaimType,
                ClaimValue = "BR-B"
            });
        await db.SaveChangesAsync();

        await SeedPaymentAsync(
            db,
            amount: 1000m,
            status: PaymentStatus.Succeeded,
            paidAtUtc: nowUtc.AddHours(-2),
            gatewayProvider: "PayMongo",
            memberUserId: "member-branch-a");

        await SeedPaymentAsync(
            db,
            amount: 2500m,
            status: PaymentStatus.Succeeded,
            paidAtUtc: nowUtc.AddHours(-1),
            gatewayProvider: "PayMongo",
            memberUserId: "member-branch-b");

        var overview = await service.GetOverviewAsync(
            fromUtc: nowUtc.AddDays(-1),
            toUtc: nowUtc.AddDays(1),
            branchId: "BR-A");

        Assert.Equal(1, overview.SuccessfulPaymentsCount);
        Assert.Equal(1000m, overview.TotalRevenue);
        Assert.Equal(1000m, overview.PayMongoRevenue);
    }

    [Fact]
    public async Task GetExpensesAsync_WithBranchId_ReturnsOnlyScopedBranchExpenses()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(GetExpensesAsync_WithBranchId_ReturnsOnlyScopedBranchExpenses));
        var db = dbHandle.Db;
        var service = new FinanceMetricsService(db);
        var nowUtc = DateTime.UtcNow;

        await SeedExpenseAsync(
            db,
            amount: 1200m,
            expenseDateUtc: nowUtc.AddDays(-1),
            category: "Utilities",
            branchId: "BR-A");
        await SeedExpenseAsync(
            db,
            amount: 2200m,
            expenseDateUtc: nowUtc.AddDays(-1),
            category: "Utilities",
            branchId: "BR-B");

        var scopedExpenses = await service.GetExpensesAsync(
            fromUtc: nowUtc.AddDays(-2),
            toUtc: nowUtc.AddDays(1),
            branchId: "BR-A");

        Assert.Single(scopedExpenses);
        Assert.Equal("BR-A", scopedExpenses[0].BranchId);
        Assert.Equal(1200m, scopedExpenses[0].Amount);
    }

    [Fact]
    public async Task GetEquipmentAssetsAsync_WithBranchId_ReturnsOnlyScopedBranchAssets()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(GetEquipmentAssetsAsync_WithBranchId_ReturnsOnlyScopedBranchAssets));
        var db = dbHandle.Db;
        var service = new FinanceMetricsService(db);
        var nowUtc = DateTime.UtcNow;

        db.GymEquipmentAssets.AddRange(
            new GymEquipmentAsset
            {
                Name = "Bike A",
                Category = "Cardio",
                BranchId = "BR-A",
                Quantity = 2,
                UnitCost = 25000m,
                UsefulLifeMonths = 60,
                IsActive = true,
                CreatedUtc = nowUtc,
                UpdatedUtc = nowUtc
            },
            new GymEquipmentAsset
            {
                Name = "Bike B",
                Category = "Cardio",
                BranchId = "BR-B",
                Quantity = 2,
                UnitCost = 25000m,
                UsefulLifeMonths = 60,
                IsActive = true,
                CreatedUtc = nowUtc,
                UpdatedUtc = nowUtc
            });
        await db.SaveChangesAsync();

        var scopedAssets = await service.GetEquipmentAssetsAsync(branchId: "BR-A");

        Assert.Single(scopedAssets);
        Assert.Equal("BR-A", scopedAssets[0].BranchId);
        Assert.Equal("Bike A", scopedAssets[0].Name);
    }

    [Fact]
    public async Task GetMonthlySnapshotsAsync_ReturnsActualMonthsAndProjection()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(GetMonthlySnapshotsAsync_ReturnsActualMonthsAndProjection));
        var db = dbHandle.Db;
        var service = new FinanceMetricsService(db);
        var nowUtc = DateTime.UtcNow;
        var currentMonthStart = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var previousMonthStart = currentMonthStart.AddMonths(-1);

        var previousInvoice = new Invoice
        {
            InvoiceNumber = $"INV-{Guid.NewGuid():N}",
            MemberUserId = "member-fin-month-prev",
            IssueDateUtc = previousMonthStart.AddDays(2),
            DueDateUtc = previousMonthStart.AddDays(12),
            Amount = 2000m,
            Status = InvoiceStatus.Paid
        };
        var currentInvoicePaid = new Invoice
        {
            InvoiceNumber = $"INV-{Guid.NewGuid():N}",
            MemberUserId = "member-fin-month-current",
            IssueDateUtc = currentMonthStart.AddDays(2),
            DueDateUtc = currentMonthStart.AddDays(12),
            Amount = 3000m,
            Status = InvoiceStatus.Paid
        };
        var draftInvoice = new Invoice
        {
            InvoiceNumber = $"INV-{Guid.NewGuid():N}",
            MemberUserId = "member-fin-month-draft",
            IssueDateUtc = currentMonthStart.AddDays(3),
            DueDateUtc = currentMonthStart.AddDays(13),
            Amount = 900m,
            Status = InvoiceStatus.Draft
        };
        var unpaidInvoice = new Invoice
        {
            InvoiceNumber = $"INV-{Guid.NewGuid():N}",
            MemberUserId = "member-fin-month-unpaid",
            IssueDateUtc = currentMonthStart.AddDays(4),
            DueDateUtc = currentMonthStart.AddDays(14),
            Amount = 1100m,
            Status = InvoiceStatus.Unpaid
        };
        var overdueInvoice = new Invoice
        {
            InvoiceNumber = $"INV-{Guid.NewGuid():N}",
            MemberUserId = "member-fin-month-overdue",
            IssueDateUtc = currentMonthStart.AddDays(5),
            DueDateUtc = currentMonthStart.AddDays(15),
            Amount = 1300m,
            Status = InvoiceStatus.Overdue
        };

        db.Invoices.AddRange(previousInvoice, currentInvoicePaid, draftInvoice, unpaidInvoice, overdueInvoice);
        await db.SaveChangesAsync();

        db.Payments.AddRange(
            new Payment
            {
                InvoiceId = previousInvoice.Id,
                Amount = 2000m,
                Method = PaymentMethod.OnlineGateway,
                Status = PaymentStatus.Succeeded,
                PaidAtUtc = previousMonthStart.AddDays(6),
                GatewayProvider = "PayMongo",
                ReferenceNumber = $"pm-{Guid.NewGuid():N}"
            },
            new Payment
            {
                InvoiceId = currentInvoicePaid.Id,
                Amount = 3000m,
                Method = PaymentMethod.OnlineGateway,
                Status = PaymentStatus.Succeeded,
                PaidAtUtc = currentMonthStart.AddDays(6),
                GatewayProvider = "PayMongo",
                ReferenceNumber = $"pm-{Guid.NewGuid():N}"
            });

        db.FinanceExpenseRecords.AddRange(
            new FinanceExpenseRecord
            {
                Name = "Consumables",
                Category = "Inventory",
                Amount = 800m,
                ExpenseDateUtc = currentMonthStart.AddDays(6),
                IsRecurring = false,
                IsActive = true,
                CreatedUtc = nowUtc,
                UpdatedUtc = nowUtc
            },
            new FinanceExpenseRecord
            {
                Name = "Branch Rent",
                Category = "Rent",
                Amount = 500m,
                ExpenseDateUtc = currentMonthStart.AddDays(7),
                IsRecurring = true,
                IsActive = true,
                CreatedUtc = nowUtc,
                UpdatedUtc = nowUtc
            });

        db.GymEquipmentAssets.Add(new GymEquipmentAsset
        {
            Name = "Lat Pulldown",
            Category = "Strength Machine",
            Quantity = 1,
            UnitCost = 1200m,
            UsefulLifeMonths = 12,
            IsActive = true,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc
        });
        await db.SaveChangesAsync();

        var snapshots = await service.GetMonthlySnapshotsAsync(months: 2, includeProjection: true);

        Assert.Equal(3, snapshots.Count);
        Assert.False(snapshots[0].IsProjected);
        Assert.False(snapshots[1].IsProjected);
        Assert.True(snapshots[2].IsProjected);

        var current = snapshots[1];
        Assert.Equal(3000m, current.Revenue);
        Assert.Equal(800m, current.CostOfServices);
        Assert.Equal(2200m, current.GrossProfit);
        Assert.Equal(500m, current.OperatingExpenses);
        Assert.Equal(100m, current.DepreciationCost);
        Assert.Equal(1600m, current.NetProfit);
        Assert.Equal(1, current.ForReviewCount);
        Assert.Equal(1, current.PendingCount);
        Assert.Equal(1, current.QueuedCount);
        Assert.Equal(1, current.ApprovedCount);
    }

    private static async Task SeedPaymentAsync(
        ApplicationDbContext db,
        decimal amount,
        PaymentStatus status,
        DateTime paidAtUtc,
        string? gatewayProvider,
        string memberUserId = "member-finance-test")
    {
        var invoice = new Invoice
        {
            InvoiceNumber = $"INV-{Guid.NewGuid():N}",
            MemberUserId = memberUserId,
            IssueDateUtc = paidAtUtc,
            DueDateUtc = paidAtUtc.AddDays(2),
            Amount = amount,
            Status = status == PaymentStatus.Succeeded ? InvoiceStatus.Paid : InvoiceStatus.Unpaid
        };

        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        db.Payments.Add(new Payment
        {
            InvoiceId = invoice.Id,
            Amount = amount,
            Method = PaymentMethod.OnlineGateway,
            Status = status,
            PaidAtUtc = paidAtUtc,
            GatewayProvider = gatewayProvider,
            ReferenceNumber = $"{gatewayProvider ?? "none"}-{Guid.NewGuid():N}"
        });

        await db.SaveChangesAsync();
    }

    private static async Task SeedExpenseAsync(
        ApplicationDbContext db,
        decimal amount,
        DateTime expenseDateUtc,
        string category,
        string? branchId = null)
    {
        var nowUtc = DateTime.UtcNow;
        db.FinanceExpenseRecords.Add(new FinanceExpenseRecord
        {
            Name = $"{category} expense",
            Category = category,
            BranchId = branchId,
            Amount = amount,
            ExpenseDateUtc = expenseDateUtc,
            IsRecurring = true,
            IsActive = true,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc
        });

        await db.SaveChangesAsync();
    }

    private static Task<InMemoryDbHandle> CreateDbContextAsync(string databaseName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"finance-tests-{databaseName}-{Guid.NewGuid():N}")
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
}
