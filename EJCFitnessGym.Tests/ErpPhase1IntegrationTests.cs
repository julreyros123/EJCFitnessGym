using System.Globalization;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Finance;
using EJCFitnessGym.Models.Integration;
using EJCFitnessGym.Models.Inventory;
using EJCFitnessGym.Services.Finance;
using EJCFitnessGym.Services.Integration;
using EJCFitnessGym.Services.Inventory;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EJCFitnessGym.Tests;

public class ErpPhase1IntegrationTests
{
    [Fact]
    public async Task CreateSaleAsync_CompletedSale_PostsRetailEntryToGeneralLedger()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(CreateSaleAsync_CompletedSale_PostsRetailEntryToGeneralLedger));
        var db = dbHandle.Db;
        var branchId = "BR-ERP-01";

        var product = new RetailProduct
        {
            Name = "Protein Shake",
            Category = "Beverages",
            Unit = "bottle",
            UnitPrice = 120m,
            CostPrice = 60m,
            StockQuantity = 20,
            ReorderLevel = 5,
            BranchId = branchId,
            IsActive = true
        };
        db.RetailProducts.Add(product);
        await db.SaveChangesAsync();

        var ledger = new GeneralLedgerService(db, NullLogger<GeneralLedgerService>.Instance);
        var outbox = new IntegrationOutboxService(db);
        var sales = new ProductSalesService(db, ledger, outbox, NullLogger<ProductSalesService>.Instance);

        var sale = await sales.CreateSaleAsync(
            branchId,
            memberUserId: null,
            customerName: "Walk-In",
            paymentMethod: PaymentMethod.Cash,
            items: [(product.Id, 2)],
            processedByUserId: "staff-1");

        var saleEntry = await db.GeneralLedgerEntries
            .Include(entry => entry.Lines)
            .ThenInclude(line => line.Account)
            .SingleAsync(entry =>
                entry.SourceType == "ProductSale" &&
                entry.SourceId == sale.Id.ToString(CultureInfo.InvariantCulture));

        var debitLine = saleEntry.Lines.Single(line => line.Debit > 0m);
        var creditLine = saleEntry.Lines.Single(line => line.Credit > 0m);

        Assert.Equal(sale.TotalAmount, debitLine.Debit);
        Assert.Equal(sale.TotalAmount, creditLine.Credit);
        Assert.Equal("1010", debitLine.Account!.Code); // Cash on Hand
        Assert.Equal("4010", creditLine.Account!.Code); // Retail Sales Revenue
    }

    [Fact]
    public async Task VoidSaleAsync_CompletedSale_CreatesRetailReversalEntry()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(VoidSaleAsync_CompletedSale_CreatesRetailReversalEntry));
        var db = dbHandle.Db;
        var branchId = "BR-ERP-02";

        db.RetailProducts.Add(new RetailProduct
        {
            Name = "Gym Towel",
            Category = "Merch",
            Unit = "piece",
            UnitPrice = 250m,
            CostPrice = 120m,
            StockQuantity = 15,
            ReorderLevel = 5,
            BranchId = branchId,
            IsActive = true
        });
        await db.SaveChangesAsync();

        var productId = await db.RetailProducts
            .Select(p => p.Id)
            .SingleAsync();

        var ledger = new GeneralLedgerService(db, NullLogger<GeneralLedgerService>.Instance);
        var outbox = new IntegrationOutboxService(db);
        var sales = new ProductSalesService(db, ledger, outbox, NullLogger<ProductSalesService>.Instance);

        var sale = await sales.CreateSaleAsync(
            branchId,
            memberUserId: null,
            customerName: "Walk-In",
            paymentMethod: PaymentMethod.Card,
            items: [(productId, 1)],
            processedByUserId: "staff-2");

        var voided = await sales.VoidSaleAsync(sale.Id, "staff-3");
        Assert.True(voided);

        var reversalEntry = await db.GeneralLedgerEntries
            .Include(entry => entry.Lines)
            .ThenInclude(line => line.Account)
            .SingleAsync(entry =>
                entry.SourceType == "ProductSaleVoid" &&
                entry.SourceId == sale.Id.ToString(CultureInfo.InvariantCulture));

        var debitLine = reversalEntry.Lines.Single(line => line.Debit > 0m);
        var creditLine = reversalEntry.Lines.Single(line => line.Credit > 0m);

        Assert.Equal("4010", debitLine.Account!.Code); // Retail Sales Revenue reversed
        Assert.Equal("1020", creditLine.Account!.Code); // Cash in Bank reversed
        Assert.Equal(sale.TotalAmount, debitLine.Debit);
        Assert.Equal(sale.TotalAmount, creditLine.Credit);
    }

    [Fact]
    public async Task SupplyRequest_ConfirmedThenInvoicedAndPaid_UpdatesStockOnlyOnce()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(SupplyRequest_ConfirmedThenInvoicedAndPaid_UpdatesStockOnlyOnce));
        var db = dbHandle.Db;
        var branchId = "BR-ERP-03";
        var outbox = new IntegrationOutboxService(db);
        var service = new SupplyRequestService(db, outbox, NullLogger<SupplyRequestService>.Instance);

        var createdRequest = await service.CreateRequestAsync(new SupplyRequest
        {
            BranchId = branchId,
            ItemName = "Resistance Bands",
            Category = "Accessories",
            RequestedQuantity = 10,
            Unit = "set",
            EstimatedUnitCost = 150m,
            RequestedByUserId = "staff-branch"
        });

        await service.ApproveAsync(createdRequest.Id, "admin-branch");
        await service.MarkOrderedAsync(createdRequest.Id);
        await service.ReceiveDraftAsync(createdRequest.Id, receivedQuantity: 8, actualUnitCost: 165m, receivedByUserId: "staff-branch");
        await service.ConfirmReceiptAsync(createdRequest.Id);

        var afterReceipt = await db.RetailProducts
            .AsNoTracking()
            .SingleAsync(product =>
                product.BranchId == branchId &&
                product.Name == "Resistance Bands" &&
                product.Unit == "set");

        Assert.Equal(8, afterReceipt.StockQuantity);
        Assert.Equal(165m, afterReceipt.CostPrice);

        _ = await service.CreateExpenseAsync(createdRequest.Id, "finance-branch");
        await service.MarkPaidAsync(createdRequest.Id);

        var afterPaid = await db.RetailProducts
            .AsNoTracking()
            .SingleAsync(product =>
                product.BranchId == branchId &&
                product.Name == "Resistance Bands" &&
                product.Unit == "set");

        Assert.Equal(8, afterPaid.StockQuantity);
        Assert.Equal(165m, afterPaid.CostPrice);
    }

    [Fact]
    public async Task FinanceAlertService_WhenAlertsAreTriggered_QueuesOutboxMessages()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(FinanceAlertService_WhenAlertsAreTriggered_QueuesOutboxMessages));
        var db = dbHandle.Db;

        var service = new FinanceAlertService(
            db,
            new StubFinanceMetricsService(),
            new IntegrationOutboxService(db),
            new NoOpEmailSender(),
            Options.Create(new FinanceAlertOptions
            {
                Enabled = true,
                EmailEnabled = false,
                CooldownMinutes = 5,
                MinHighSeverityAnomalies = 1
            }),
            NullLogger<FinanceAlertService>.Instance);

        var result = await service.EvaluateAndNotifyAsync("erp.phase1.test");

        Assert.Equal(2, result.AlertsSent);

        var alertLogs = await db.FinanceAlertLogs
            .AsNoTracking()
            .ToListAsync();
        Assert.Equal(2, alertLogs.Count);
        Assert.All(alertLogs, log => Assert.True(log.RealtimePublished));

        var outboxMessages = await db.IntegrationOutboxMessages
            .AsNoTracking()
            .Where(message => message.EventType == "finance.alert")
            .ToListAsync();

        Assert.Equal(4, outboxMessages.Count); // 2 alerts x (Finance role + BackOffice)
        Assert.Equal(2, outboxMessages.Count(message => message.Target == IntegrationOutboxTarget.Role));
        Assert.Equal(2, outboxMessages.Count(message => message.Target == IntegrationOutboxTarget.BackOffice));
    }

    private static async Task<SqliteDbHandle> CreateDbContextAsync(string databaseName)
    {
        var connection = new SqliteConnection($"Data Source={databaseName};Mode=Memory;Cache=Shared");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .EnableSensitiveDataLogging()
            .Options;

        var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return new SqliteDbHandle(db, connection);
    }

    private sealed class SqliteDbHandle : IAsyncDisposable
    {
        public ApplicationDbContext Db { get; }
        private readonly SqliteConnection _connection;

        public SqliteDbHandle(ApplicationDbContext db, SqliteConnection connection)
        {
            Db = db;
            _connection = connection;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class StubFinanceMetricsService : IFinanceMetricsService
    {
        public Task<FinanceOverviewDto> GetOverviewAsync(
            DateTime? fromUtc = null,
            DateTime? toUtc = null,
            CancellationToken cancellationToken = default,
            string? branchId = null)
        {
            return Task.FromResult(new FinanceOverviewDto());
        }

        public Task<FinanceInsightsDto> GetInsightsAsync(
            int lookbackDays = 120,
            int forecastDays = 30,
            CancellationToken cancellationToken = default,
            string? branchId = null)
        {
            var nowUtc = DateTime.UtcNow;
            return Task.FromResult(new FinanceInsightsDto
            {
                GeneratedAtUtc = nowUtc,
                LookbackFromUtc = nowUtc.AddDays(-lookbackDays),
                LookbackToUtc = nowUtc,
                LookbackDays = lookbackDays,
                ForecastDays = forecastDays,
                ForecastNet = -1200m,
                ForecastRevenue = 8000m,
                ForecastTotalExpense = 9200m,
                RiskLevel = "High",
                GainOrLossSignal = "Projected Loss",
                Anomalies =
                [
                    new FinanceAnomalyDto
                    {
                        DateUtc = nowUtc.Date,
                        Type = "Revenue Spike",
                        ActualValue = 10000m,
                        ExpectedValue = 2500m,
                        DeviationPercent = 300m,
                        Severity = "High",
                        Description = "Significant deviation detected."
                    }
                ]
            });
        }

        public Task<IReadOnlyList<GymEquipmentAsset>> GetEquipmentAssetsAsync(
            string? branchId = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<GymEquipmentAsset>>(Array.Empty<GymEquipmentAsset>());
        }

        public Task<IReadOnlyList<FinanceExpenseRecord>> GetExpensesAsync(
            DateTime? fromUtc = null,
            DateTime? toUtc = null,
            string? branchId = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<FinanceExpenseRecord>>(Array.Empty<FinanceExpenseRecord>());
        }

        public Task<IReadOnlyList<FinanceMonthlySnapshotDto>> GetMonthlySnapshotsAsync(
            int months = 6,
            bool includeProjection = false,
            CancellationToken cancellationToken = default,
            string? branchId = null)
        {
            return Task.FromResult<IReadOnlyList<FinanceMonthlySnapshotDto>>(Array.Empty<FinanceMonthlySnapshotDto>());
        }

        public Task<EquipmentSeedResultDto> SeedMediumGymSampleAsync(
            string? branchId = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new EquipmentSeedResultDto());
        }
    }

    private sealed class NoOpEmailSender : IEmailSender
    {
        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            return Task.CompletedTask;
        }
    }
}
