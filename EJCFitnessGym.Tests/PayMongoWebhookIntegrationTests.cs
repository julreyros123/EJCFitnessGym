using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using EJCFitnessGym.Controllers;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Models.Finance;
using EJCFitnessGym.Services.Finance;
using EJCFitnessGym.Services.Integration;
using EJCFitnessGym.Services.Memberships;
using EJCFitnessGym.Services.Payments;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
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

    [Fact]
    public async Task Receive_PaidWebhookWithUnderpayment_DoesNotCloseInvoiceOrActivateMembership()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(Receive_PaidWebhookWithUnderpayment_DoesNotCloseInvoiceOrActivateMembership));
        var db = dbHandle.Db;
        await SeedCheckoutPaymentAsync(db, checkoutSessionId: "cs_step3_underpaid", amount: 2000m, planId: 300);

        var controller = CreateController(
            db,
            new IntegrationOutboxService(db));

        var payload = BuildPaidWebhookPayload(
            eventId: "evt_underpaid_001",
            checkoutSessionId: "cs_step3_underpaid",
            paymentId: "pay_underpaid_001",
            planId: 300,
            amountMinorUnit: 150000);

        SetJsonRequest(controller, payload);
        var result = await controller.Receive(CancellationToken.None);
        Assert.IsType<OkResult>(result);

        var payment = await db.Payments.Include(p => p.Invoice).SingleAsync(p => p.ReferenceNumber == "cs_step3_underpaid");
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(1500m, payment.Amount);

        Assert.NotNull(payment.Invoice);
        Assert.Equal(InvoiceStatus.Unpaid, payment.Invoice!.Status);
        Assert.False(await db.MemberSubscriptions.AnyAsync(s => s.ExternalSubscriptionId == "cs_step3_underpaid"));

        Assert.True(await db.IntegrationOutboxMessages.AnyAsync(m => m.EventType == "payment.succeeded"));
        Assert.True(await db.IntegrationOutboxMessages.AnyAsync(m => m.EventType == "membership.reconciliation.warning"));
        Assert.False(await db.IntegrationOutboxMessages.AnyAsync(m => m.EventType == "membership.activated"));
    }

    [Fact]
    public async Task Receive_FailedWebhookForOlderCheckout_DoesNotReopenAlreadyPaidInvoice()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(Receive_FailedWebhookForOlderCheckout_DoesNotReopenAlreadyPaidInvoice));
        var db = dbHandle.Db;
        await SeedCheckoutPaymentAsync(db, checkoutSessionId: "cs_old_failed", amount: 2000m, planId: 350);

        var invoiceId = await db.Payments
            .Where(payment => payment.ReferenceNumber == "cs_old_failed")
            .Select(payment => payment.InvoiceId)
            .SingleAsync();

        db.Payments.Add(new Payment
        {
            InvoiceId = invoiceId,
            Amount = 2000m,
            Method = PaymentMethod.OnlineGateway,
            Status = PaymentStatus.Pending,
            PaidAtUtc = DateTime.UtcNow.AddMinutes(1),
            GatewayProvider = "PayMongo",
            ReferenceNumber = "cs_new_paid"
        });
        await db.SaveChangesAsync();

        var controller = CreateController(
            db,
            new IntegrationOutboxService(db));

        var paidPayload = BuildPaidWebhookPayload(
            eventId: "evt_paid_new_001",
            checkoutSessionId: "cs_new_paid",
            paymentId: "pay_new_001",
            planId: 350,
            amountMinorUnit: 200000);

        SetJsonRequest(controller, paidPayload);
        var paidResult = await controller.Receive(CancellationToken.None);
        Assert.IsType<OkResult>(paidResult);
        db.ChangeTracker.Clear();

        var failedPayload = BuildFailedWebhookPayload(
            eventId: "evt_failed_old_001",
            checkoutSessionId: "cs_old_failed",
            paymentId: "pay_old_001");

        SetJsonRequest(controller, failedPayload);
        var failedResult = await controller.Receive(CancellationToken.None);
        if (failedResult is not OkResult)
        {
            var failedReceipt = await db.InboundWebhookReceipts
                .AsNoTracking()
                .SingleAsync(receipt => receipt.Provider == "PayMongo" && receipt.EventKey == "evt_failed_old_001");

            throw new Xunit.Sdk.XunitException(
                $"Expected OkResult for failed webhook replay. Actual: {failedResult.GetType().Name}. Receipt status: {failedReceipt.Status}. Notes: {failedReceipt.Notes}");
        }

        var oldPayment = await db.Payments
            .Include(payment => payment.Invoice)
            .SingleAsync(payment => payment.ReferenceNumber == "cs_old_failed");

        Assert.Equal(PaymentStatus.Failed, oldPayment.Status);
        Assert.NotNull(oldPayment.Invoice);
        Assert.Equal(InvoiceStatus.Paid, oldPayment.Invoice!.Status);
    }

    [Fact]
    public async Task Receive_ProductionWithoutWebhookSecret_RejectsWebhook()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(Receive_ProductionWithoutWebhookSecret_RejectsWebhook));
        var db = dbHandle.Db;
        await SeedCheckoutPaymentAsync(db, checkoutSessionId: "cs_prod_missing_secret", amount: 1500m, planId: 360);

        var controller = CreateController(
            db,
            new IntegrationOutboxService(db),
            environmentName: Environments.Production);

        var payload = BuildPaidWebhookPayload(
            eventId: "evt_prod_missing_secret",
            checkoutSessionId: "cs_prod_missing_secret",
            paymentId: "pay_prod_missing_secret",
            planId: 360,
            amountMinorUnit: 150000);

        SetJsonRequest(controller, payload);
        var result = await controller.Receive(CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.False(await db.InboundWebhookReceipts.AnyAsync());
    }

    [Fact]
    public async Task Receive_ProductionWithValidWebhookSignature_ProcessesWebhook()
    {
        await using var dbHandle = await CreateDbContextAsync(nameof(Receive_ProductionWithValidWebhookSignature_ProcessesWebhook));
        var db = dbHandle.Db;
        await SeedCheckoutPaymentAsync(db, checkoutSessionId: "cs_prod_signed", amount: 1800m, planId: 370);

        const string webhookSecret = "whsec_test_prod_123";
        var controller = CreateController(
            db,
            new IntegrationOutboxService(db),
            environmentName: Environments.Production,
            webhookSecret: webhookSecret);

        var payload = BuildPaidWebhookPayload(
            eventId: "evt_prod_signed",
            checkoutSessionId: "cs_prod_signed",
            paymentId: "pay_prod_signed",
            planId: 370,
            amountMinorUnit: 180000);

        SetJsonRequest(controller, payload, CreatePayMongoSignatureHeader(webhookSecret, payload));
        var result = await controller.Receive(CancellationToken.None);

        Assert.IsType<OkResult>(result);
        var payment = await db.Payments.Include(p => p.Invoice).SingleAsync(p => p.ReferenceNumber == "cs_prod_signed");
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.NotNull(payment.Invoice);
        Assert.Equal(InvoiceStatus.Paid, payment.Invoice!.Status);
    }

    private static PayMongoWebhookController CreateController(
        ApplicationDbContext db,
        IIntegrationOutbox outbox,
        string environmentName = "Development",
        bool requireWebhookSignature = false,
        string? webhookSecret = null)
    {
        var membershipService = new MembershipService(db);
        var financeAlertService = new NoOpFinanceAlertService();
        var options = Options.Create(new PayMongoOptions
        {
            RequireWebhookSignature = requireWebhookSignature,
            WebhookSecret = webhookSecret
        });

        return new PayMongoWebhookController(
            db,
            membershipService,
            financeAlertService,
            new NoOpGeneralLedgerService(),
            outbox,
            new NoOpEmailSender(),
            options,
            new TestHostEnvironment(environmentName),
            NullLogger<PayMongoWebhookController>.Instance);
    }

    private static void SetJsonRequest(ControllerBase controller, string json, string? signatureHeader = null)
    {
        var context = new DefaultHttpContext();
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Request.Body = new MemoryStream(bytes);
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = bytes.Length;
        if (!string.IsNullOrWhiteSpace(signatureHeader))
        {
            context.Request.Headers["PayMongo-Signature"] = signatureHeader;
        }

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = context
        };
    }

    private static string CreatePayMongoSignatureHeader(string webhookSecret, string rawBody)
    {
        var timestampUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signedPayload = $"{timestampUnix}.{rawBody}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload))).ToLowerInvariant();
        return $"t={timestampUnix},te={signature}";
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
                                ["plan_name"] = "Basic",
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

    private static string BuildFailedWebhookPayload(
        string eventId,
        string checkoutSessionId,
        string paymentId)
    {
        var payload = new
        {
            data = new
            {
                id = eventId,
                attributes = new
                {
                    type = "checkout_session.expired",
                    data = new
                    {
                        id = checkoutSessionId,
                        attributes = new
                        {
                            payments = new[]
                            {
                                new
                                {
                                    id = paymentId
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

    private sealed class NoOpEmailSender : IEmailSender
    {
        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpGeneralLedgerService : IGeneralLedgerService
    {
        public Task EnsureDefaultAccountsAsync(string? branchId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<GeneralLedgerAccount>> GetActiveAccountsAsync(string branchId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<GeneralLedgerAccount>>(Array.Empty<GeneralLedgerAccount>());
        }

        public Task PostPaymentReceiptAsync(int paymentId, string? actorUserId = null, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task PostOperatingExpenseAsync(int expenseId, string? actorUserId = null, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task PostRetailSaleAsync(int productSaleId, string? actorUserId = null, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task PostRetailSaleVoidAsync(int productSaleId, string? actorUserId = null, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<GeneralLedgerEntry> CreateManualEntryAsync(
            string branchId,
            DateTime entryDateUtc,
            string description,
            int debitAccountId,
            int creditAccountId,
            decimal amount,
            string? memo = null,
            string? actorUserId = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new GeneralLedgerEntry());
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

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "EJCFitnessGym.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
