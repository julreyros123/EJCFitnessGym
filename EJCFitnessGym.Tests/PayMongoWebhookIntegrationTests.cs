using System.Text;
using System.Text.Json;
using EJCFitnessGym.Controllers;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Services.Finance;
using EJCFitnessGym.Services.Integration;
using EJCFitnessGym.Services.Memberships;
using EJCFitnessGym.Services.Payments;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EJCFitnessGym.Tests;

public class PayMongoWebhookIntegrationTests
{
    [Fact]
    public async Task Receive_DuplicatePaidWebhook_IsProcessedOnce()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(Receive_DuplicatePaidWebhook_IsProcessedOnce));
        var db = dbHandle.Db;
        await SeedCheckoutPaymentAsync(db, checkoutSessionId: "cs_step3_dup", amount: 1000m, planId: 100);

        var controller = CreateController(
            db,
            new IntegrationOutboxService(db));

        var payload = BuildPaidWebhookPayload(
            eventId: "evt_dup_001",
            checkoutSessionId: "cs_step3_dup",
            paymentId: "pay_dup_001",
            planId: 100,
            amountMinorUnit: 100000);

        SetJsonRequest(controller, payload);
        var first = await controller.Receive(CancellationToken.None);
        Assert.IsType<OkResult>(first);

        var outboxCountAfterFirst = await db.IntegrationOutboxMessages.CountAsync();
        Assert.True(outboxCountAfterFirst >= 2);
        db.ChangeTracker.Clear();

        SetJsonRequest(controller, payload);
        var second = await controller.Receive(CancellationToken.None);
        Assert.IsType<OkResult>(second);

        var receipt = await db.InboundWebhookReceipts.SingleAsync(r => r.Provider == "PayMongo" && r.EventKey == "evt_dup_001");
        Assert.Equal("Processed", receipt.Status);
        Assert.Equal(1, receipt.AttemptCount);

        var outboxCountAfterSecond = await db.IntegrationOutboxMessages.CountAsync();
        Assert.Equal(outboxCountAfterFirst, outboxCountAfterSecond);
    }

    [Fact]
    public async Task Receive_PaidWebhookFailureThenRetry_RecoversAndProcesses()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(Receive_PaidWebhookFailureThenRetry_RecoversAndProcesses));
        var db = dbHandle.Db;
        await SeedCheckoutPaymentAsync(db, checkoutSessionId: "cs_step3_retry", amount: 2000m, planId: 200);

        var realOutbox = new IntegrationOutboxService(db);
        var flakyOutbox = new FlakyOutbox(realOutbox);
        var controller = CreateController(db, flakyOutbox);

        var payload = BuildPaidWebhookPayload(
            eventId: "evt_retry_001",
            checkoutSessionId: "cs_step3_retry",
            paymentId: "pay_retry_001",
            planId: 200,
            amountMinorUnit: 200000);

        SetJsonRequest(controller, payload);
        var first = await controller.Receive(CancellationToken.None);
        var firstResult = Assert.IsType<StatusCodeResult>(first);
        Assert.Equal(StatusCodes.Status500InternalServerError, firstResult.StatusCode);

        var failedReceipt = await db.InboundWebhookReceipts.SingleAsync(r => r.Provider == "PayMongo" && r.EventKey == "evt_retry_001");
        Assert.Equal("Failed", failedReceipt.Status);
        Assert.Equal(1, failedReceipt.AttemptCount);
        db.ChangeTracker.Clear();

        SetJsonRequest(controller, payload);
        var second = await controller.Receive(CancellationToken.None);
        Assert.IsType<OkResult>(second);

        var recoveredReceipt = await db.InboundWebhookReceipts.SingleAsync(r => r.Provider == "PayMongo" && r.EventKey == "evt_retry_001");
        Assert.Equal("Processed", recoveredReceipt.Status);
        Assert.Equal(2, recoveredReceipt.AttemptCount);

        var payment = await db.Payments.Include(p => p.Invoice).SingleAsync(p => p.ReferenceNumber == "cs_step3_retry");
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.NotNull(payment.Invoice);
        Assert.Equal(InvoiceStatus.Paid, payment.Invoice!.Status);
        Assert.True(await db.IntegrationOutboxMessages.AnyAsync(m => m.EventType == "payment.succeeded"));
    }

    private static PayMongoWebhookController CreateController(ApplicationDbContext db, IIntegrationOutbox outbox)
    {
        var membershipService = new MembershipService(db);
        var financeAlertService = new NoOpFinanceAlertService();
        var options = Options.Create(new PayMongoOptions
        {
            RequireWebhookSignature = false,
            WebhookSecret = null
        });

        return new PayMongoWebhookController(
            db,
            membershipService,
            financeAlertService,
            outbox,
            options,
            NullLogger<PayMongoWebhookController>.Instance);
    }

    private static void SetJsonRequest(ControllerBase controller, string json)
    {
        var context = new DefaultHttpContext();
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = bytes.Length;
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = context
        };
    }

    private static string BuildPaidWebhookPayload(
        string eventId,
        string checkoutSessionId,
        string paymentId,
        int planId,
        int amountMinorUnit)
    {
        var payload = new
        {
            data = new
            {
                id = eventId,
                attributes = new
                {
                    type = "checkout_session.payment.paid",
                    data = new
                    {
                        id = checkoutSessionId,
                        attributes = new
                        {
                            metadata = new Dictionary<string, string>
                            {
                                ["member_user_id"] = "member-user-1",
                                ["plan_id"] = planId.ToString(),
                                ["plan_name"] = "Starter",
                                ["customer_id"] = "cust_test_1"
                            },
                            payments = new[]
                            {
                                new
                                {
                                    id = paymentId,
                                    attributes = new
                                    {
                                        amount = amountMinorUnit
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(payload);
    }

    private static async Task SeedCheckoutPaymentAsync(
        ApplicationDbContext db,
        string checkoutSessionId,
        decimal amount,
        int planId)
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

        var invoice = new Invoice
        {
            InvoiceNumber = $"INV-{planId}-{Guid.NewGuid():N}",
            MemberUserId = "member-user-1",
            IssueDateUtc = DateTime.UtcNow,
            DueDateUtc = DateTime.UtcNow.AddDays(1),
            Amount = amount,
            Status = InvoiceStatus.Unpaid,
            Notes = $"Subscription purchase [plan:{planId}]"
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();

        db.Payments.Add(new Payment
        {
            InvoiceId = invoice.Id,
            Amount = amount,
            Method = PaymentMethod.OnlineGateway,
            Status = PaymentStatus.Pending,
            PaidAtUtc = DateTime.UtcNow,
            GatewayProvider = "PayMongo",
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

    private sealed class NoOpFinanceAlertService : IFinanceAlertService
    {
        public Task<FinanceAlertEvaluationResultDto> EvaluateAndNotifyAsync(string trigger, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new FinanceAlertEvaluationResultDto
            {
                Enabled = false,
                Trigger = trigger,
                EvaluatedAtUtc = DateTime.UtcNow
            });
        }
    }

    private sealed class FlakyOutbox : IIntegrationOutbox
    {
        private readonly IIntegrationOutbox _inner;
        private int _failuresRemaining = 1;

        public FlakyOutbox(IIntegrationOutbox inner)
        {
            _inner = inner;
        }

        public Task EnqueueBackOfficeAsync(string eventType, string message, object? data = null, CancellationToken cancellationToken = default)
        {
            if (_failuresRemaining > 0)
            {
                _failuresRemaining--;
                throw new InvalidOperationException("Simulated outbox enqueue failure.");
            }

            return _inner.EnqueueBackOfficeAsync(eventType, message, data, cancellationToken);
        }

        public Task EnqueueRoleAsync(string role, string eventType, string message, object? data = null, CancellationToken cancellationToken = default)
            => _inner.EnqueueRoleAsync(role, eventType, message, data, cancellationToken);

        public Task EnqueueUserAsync(string userId, string eventType, string message, object? data = null, CancellationToken cancellationToken = default)
            => _inner.EnqueueUserAsync(userId, eventType, message, data, cancellationToken);
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
