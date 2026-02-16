using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Models.Finance;
using EJCFitnessGym.Services.Finance;
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

    private static async Task SeedPaymentAsync(
        ApplicationDbContext db,
        decimal amount,
        PaymentStatus status,
        DateTime paidAtUtc,
        string? gatewayProvider)
    {
        var invoice = new Invoice
        {
            InvoiceNumber = $"INV-{Guid.NewGuid():N}",
            MemberUserId = "member-finance-test",
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
        string category)
    {
        var nowUtc = DateTime.UtcNow;
        db.FinanceExpenseRecords.Add(new FinanceExpenseRecord
        {
            Name = $"{category} expense",
            Category = category,
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
