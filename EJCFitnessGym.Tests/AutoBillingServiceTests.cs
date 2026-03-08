using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Services.Payments;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EJCFitnessGym.Tests;

public class AutoBillingServiceTests
{
    [Fact]
    public async Task ChargeInvoiceAsync_DisablesUnsupportedPayMongoAutoBilling_AndSkipsCharge()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(ChargeInvoiceAsync_DisablesUnsupportedPayMongoAutoBilling_AndSkipsCharge));
        var db = dbHandle.Db;

        var invoice = new Invoice
        {
            InvoiceNumber = $"INV-AUTO-{Guid.NewGuid():N}",
            MemberUserId = "member-1",
            IssueDateUtc = DateTime.UtcNow.AddDays(-2),
            DueDateUtc = DateTime.UtcNow.AddDays(-1),
            Amount = 999m,
            Status = InvoiceStatus.Unpaid
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        db.SavedPaymentMethods.Add(new SavedPaymentMethod
        {
            MemberUserId = "member-1",
            GatewayProvider = "PayMongo",
            GatewayPaymentMethodId = "pm_saved_checkout_001",
            PaymentMethodType = "card",
            DisplayLabel = "Visa ****4242",
            IsDefault = true,
            AutoBillingEnabled = true,
            IsActive = true,
            CreatedUtc = DateTime.UtcNow.AddDays(-3)
        });
        await db.SaveChangesAsync();

        var payMongoClient = new PayMongoClient(
            new HttpClient(),
            Options.Create(new PayMongoOptions { SecretKey = "sk_test_placeholder" }));
        var service = new AutoBillingService(
            db,
            payMongoClient,
            outbox: null,
            NullLogger<AutoBillingService>.Instance);

        var result = await service.ChargeInvoiceAsync(invoice.Id);

        Assert.False(result.Success);
        Assert.Equal(PayMongoBillingCapabilities.UnsupportedAutoBillingReason, result.SkippedReason);

        var savedMethod = await db.SavedPaymentMethods.SingleAsync();
        Assert.False(savedMethod.AutoBillingEnabled);
        Assert.Equal(0, await db.Payments.CountAsync());
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
}
