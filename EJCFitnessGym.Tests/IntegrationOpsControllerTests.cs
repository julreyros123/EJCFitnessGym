using System.Security.Claims;
using EJCFitnessGym.Controllers;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Models.Integration;
using EJCFitnessGym.Services.Integration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Tests;

public class IntegrationOpsControllerTests
{
    [Fact]
    public async Task RetryOutboxMessage_FailedMessage_MovesBackToPending()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(RetryOutboxMessage_FailedMessage_MovesBackToPending));
        var db = dbHandle.Db;

        var failedMessage = new IntegrationOutboxMessage
        {
            Target = IntegrationOutboxTarget.BackOffice,
            EventType = "payment.failed",
            Message = "Failure notification",
            Status = IntegrationOutboxStatus.Failed,
            AttemptCount = 4,
            LastError = "Simulated error",
            NextAttemptUtc = DateTime.UtcNow.AddHours(1),
            CreatedUtc = DateTime.UtcNow.AddMinutes(-5),
            UpdatedUtc = DateTime.UtcNow.AddMinutes(-1)
        };

        db.IntegrationOutboxMessages.Add(failedMessage);
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var result = await controller.RetryOutboxMessage(failedMessage.Id, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);

        var updated = await db.IntegrationOutboxMessages.SingleAsync(m => m.Id == failedMessage.Id);
        Assert.Equal(IntegrationOutboxStatus.Pending, updated.Status);
        Assert.Null(updated.LastError);
        Assert.True(updated.NextAttemptUtc <= DateTime.UtcNow.AddSeconds(5));
    }

    [Fact]
    public async Task ReplayPayMongoWebhook_ByReference_CreatesReceiptAndQueuesSuccessEvents()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(ReplayPayMongoWebhook_ByReference_CreatesReceiptAndQueuesSuccessEvents));
        var db = dbHandle.Db;

        await SeedPaidPaymentAsync(db, checkoutSessionId: "cs_manual_replay_001", planId: 301, amount: 1500m);

        var controller = CreateController(db);
        SetAuthenticatedUser(controller, "ops@ejcfit.local");

        var request = new IntegrationOpsController.ReplayPayMongoWebhookRequest
        {
            Reference = "cs_manual_replay_001",
            Force = false
        };

        var result = await controller.ReplayPayMongoWebhook(request, CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);

        var receipt = await db.InboundWebhookReceipts
            .SingleAsync(r => r.Provider == "PayMongo" && r.ExternalReference == "cs_manual_replay_001");
        Assert.Equal("Processed", receipt.Status);
        Assert.True(receipt.AttemptCount >= 1);

        Assert.True(await db.IntegrationOutboxMessages.AnyAsync(m => m.EventType == "payment.succeeded"));
        Assert.True(await db.IntegrationOutboxMessages.AnyAsync(m => m.EventType == "membership.activated"));
    }

    [Fact]
    public async Task ReplayPayMongoWebhook_ProcessedReceiptWithoutForce_ReturnsConflict()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(ReplayPayMongoWebhook_ProcessedReceiptWithoutForce_ReturnsConflict));
        var db = dbHandle.Db;

        await SeedPaidPaymentAsync(db, checkoutSessionId: "cs_replay_conflict_001", planId: 302, amount: 1700m);

        db.InboundWebhookReceipts.Add(new InboundWebhookReceipt
        {
            Provider = "PayMongo",
            EventKey = "evt_conflict_001",
            EventType = "checkout_session.payment.paid",
            ExternalReference = "cs_replay_conflict_001",
            Status = "Processed",
            AttemptCount = 1,
            FirstReceivedUtc = DateTime.UtcNow.AddMinutes(-20),
            LastAttemptUtc = DateTime.UtcNow.AddMinutes(-20),
            ProcessedUtc = DateTime.UtcNow.AddMinutes(-20),
            CreatedUtc = DateTime.UtcNow.AddMinutes(-20),
            UpdatedUtc = DateTime.UtcNow.AddMinutes(-20)
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        var request = new IntegrationOpsController.ReplayPayMongoWebhookRequest
        {
            EventKey = "evt_conflict_001",
            Force = false
        };

        var result = await controller.ReplayPayMongoWebhook(request, CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(result);
    }

    private static IntegrationOpsController CreateController(ApplicationDbContext db)
    {
        return new IntegrationOpsController(db, new IntegrationOutboxService(db));
    }

    private static void SetAuthenticatedUser(ControllerBase controller, string username)
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.Name, username)
            },
            authenticationType: "TestAuth");

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    private static async Task SeedPaidPaymentAsync(
        ApplicationDbContext db,
        string checkoutSessionId,
        int planId,
        decimal amount)
    {
        db.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Id = planId,
            Name = $"Plan-{planId}",
            Price = amount,
            BillingCycle = BillingCycle.Monthly,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        });

        var subscription = new MemberSubscription
        {
            MemberUserId = "member-user-test",
            SubscriptionPlanId = planId,
            Status = SubscriptionStatus.Active,
            StartDateUtc = DateTime.UtcNow.AddDays(-1),
            EndDateUtc = DateTime.UtcNow.AddDays(29),
            ExternalSubscriptionId = checkoutSessionId,
            ExternalCustomerId = "cust_replay_001"
        };
        db.MemberSubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        var invoice = new Invoice
        {
            InvoiceNumber = $"INV-REPLAY-{Guid.NewGuid():N}",
            MemberUserId = "member-user-test",
            MemberSubscriptionId = subscription.Id,
            IssueDateUtc = DateTime.UtcNow,
            DueDateUtc = DateTime.UtcNow.AddDays(1),
            Amount = amount,
            Status = InvoiceStatus.Paid,
            Notes = $"Replay seed [plan:{planId}]"
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        db.Payments.Add(new Payment
        {
            InvoiceId = invoice.Id,
            Amount = amount,
            Method = PaymentMethod.OnlineGateway,
            Status = PaymentStatus.Succeeded,
            PaidAtUtc = DateTime.UtcNow,
            GatewayProvider = "PayMongo",
            GatewayPaymentId = $"pay-{Guid.NewGuid():N}",
            ReferenceNumber = checkoutSessionId
        });

        await db.SaveChangesAsync();
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
