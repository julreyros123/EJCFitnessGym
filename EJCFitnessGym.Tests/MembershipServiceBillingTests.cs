using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Services.Integration;
using EJCFitnessGym.Services.Memberships;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Tests;

public class MembershipServiceBillingTests
{
    [Fact]
    public async Task RunLifecycleMaintenance_CreatesSingleRenewalInvoice_ForActiveSubscription()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(RunLifecycleMaintenance_CreatesSingleRenewalInvoice_ForActiveSubscription));
        var db = dbHandle.Db;

        var nowUtc = DateTime.UtcNow;
        var plan = new SubscriptionPlan
        {
            Name = "Starter",
            Description = "Starter monthly plan",
            Price = 999m,
            BillingCycle = BillingCycle.Monthly,
            IsActive = true,
            CreatedAtUtc = nowUtc
        };
        db.SubscriptionPlans.Add(plan);
        await db.SaveChangesAsync();

        var subscription = new MemberSubscription
        {
            MemberUserId = "member-1",
            SubscriptionPlanId = plan.Id,
            StartDateUtc = nowUtc,
            EndDateUtc = nowUtc.AddDays(10),
            Status = SubscriptionStatus.Active
        };
        db.MemberSubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var service = new MembershipService(db, new IntegrationOutboxService(db));

        var firstRun = await service.RunLifecycleMaintenanceAsync(nowUtc);
        Assert.Equal(1, firstRun.GeneratedRenewalInvoices);

        var invoices = await db.Invoices
            .Where(i => i.MemberSubscriptionId == subscription.Id)
            .ToListAsync();
        Assert.Single(invoices);
        Assert.Equal(InvoiceStatus.Unpaid, invoices[0].Status);
        Assert.Equal(subscription.EndDateUtc!.Value, invoices[0].DueDateUtc);
        Assert.Equal(plan.Price, invoices[0].Amount);

        var secondRun = await service.RunLifecycleMaintenanceAsync(nowUtc.AddMinutes(5));
        Assert.Equal(0, secondRun.GeneratedRenewalInvoices);
        Assert.Single(await db.Invoices.Where(i => i.MemberSubscriptionId == subscription.Id).ToListAsync());
    }

    [Fact]
    public async Task RunLifecycleMaintenance_QueuesThreeDayReminderOnExactDay_OnlyOncePerInvoice()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(RunLifecycleMaintenance_QueuesThreeDayReminderOnExactDay_OnlyOncePerInvoice));
        var db = dbHandle.Db;

        var nowUtc = DateTime.UtcNow;
        var reminderRunUtc = new DateTime(
            nowUtc.Year,
            nowUtc.Month,
            nowUtc.Day,
            8,
            0,
            0,
            DateTimeKind.Utc);

        var invoice = new Invoice
        {
            InvoiceNumber = $"INV-REM-{Guid.NewGuid():N}",
            MemberUserId = "member-2",
            IssueDateUtc = reminderRunUtc,
            DueDateUtc = reminderRunUtc.Date.AddDays(3).AddHours(10),
            Amount = 1499m,
            Status = InvoiceStatus.Unpaid,
            Notes = "Renewal invoice for Pro [plan:2]"
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        var service = new MembershipService(db, new IntegrationOutboxService(db));

        var earlyRun = await service.RunLifecycleMaintenanceAsync(reminderRunUtc.AddDays(-1));
        Assert.Equal(0, earlyRun.ThreeDayRemindersQueued);

        var firstRun = await service.RunLifecycleMaintenanceAsync(reminderRunUtc);
        Assert.Equal(1, firstRun.ThreeDayRemindersQueued);

        var outboxCountAfterFirst = await db.IntegrationOutboxMessages.CountAsync();
        Assert.Equal(2, outboxCountAfterFirst); // user + backoffice

        var invoiceAfterFirst = await db.Invoices.SingleAsync(i => i.Id == invoice.Id);
        Assert.Contains("[reminder-3d:", invoiceAfterFirst.Notes ?? string.Empty, StringComparison.Ordinal);

        var secondRun = await service.RunLifecycleMaintenanceAsync(reminderRunUtc.AddMinutes(10));
        Assert.Equal(0, secondRun.ThreeDayRemindersQueued);
        Assert.Equal(outboxCountAfterFirst, await db.IntegrationOutboxMessages.CountAsync());
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
