using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Services.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EJCFitnessGym.Services.Payments
{
    public interface IAutoBillingService
    {
        /// <summary>
        /// Processes automatic billing for all due invoices.
        /// </summary>
        Task<AutoBillingRunResult> ProcessDueBillingAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Charges a specific invoice using the member's saved payment method.
        /// </summary>
        Task<AutoBillingChargeResult> ChargeInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the saved payment method for a member.
        /// </summary>
        Task<SavedPaymentMethod?> GetDefaultPaymentMethodAsync(string memberUserId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Saves a payment method for a member.
        /// </summary>
        Task<SavedPaymentMethod> SavePaymentMethodAsync(
            string memberUserId,
            string gatewayPaymentMethodId,
            string paymentMethodType,
            string? displayLabel = null,
            string? customerId = null,
            bool enableAutoBilling = true,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Disables auto-billing for a member.
        /// </summary>
        Task DisableAutoBillingAsync(string memberUserId, CancellationToken cancellationToken = default);
    }

    public class AutoBillingService : IAutoBillingService
    {
        private readonly ApplicationDbContext _db;
        private readonly PayMongoClient _payMongoClient;
        private readonly IIntegrationOutbox? _outbox;
        private readonly ILogger<AutoBillingService> _logger;

        // Max retry attempts before disabling the payment method
        private const int MaxFailedAttempts = 3;

        // Grace period after due date before attempting auto-charge (in hours)
        private const int GracePeriodHours = 1;

        public AutoBillingService(
            ApplicationDbContext db,
            PayMongoClient payMongoClient,
            IIntegrationOutbox? outbox,
            ILogger<AutoBillingService> logger)
        {
            _db = db;
            _payMongoClient = payMongoClient;
            _outbox = outbox;
            _logger = logger;
        }

        public async Task<AutoBillingRunResult> ProcessDueBillingAsync(CancellationToken cancellationToken = default)
        {
            var nowUtc = DateTime.UtcNow;
            var graceThresholdUtc = nowUtc.AddHours(-GracePeriodHours);

            // Find all unpaid/overdue invoices that are past due and have not been attempted recently
            var dueInvoices = await _db.Invoices
                .AsNoTracking()
                .Where(invoice =>
                    (invoice.Status == InvoiceStatus.Unpaid || invoice.Status == InvoiceStatus.Overdue) &&
                    invoice.DueDateUtc <= graceThresholdUtc &&
                    !string.IsNullOrEmpty(invoice.MemberUserId))
                .OrderBy(invoice => invoice.DueDateUtc)
                .Take(100) // Process in batches
                .ToListAsync(cancellationToken);

            var result = new AutoBillingRunResult
            {
                ProcessedAtUtc = nowUtc,
                TotalDueInvoices = dueInvoices.Count
            };

            foreach (var invoice in dueInvoices)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    var chargeResult = await ChargeInvoiceAsync(invoice.Id, cancellationToken);
                    
                    if (chargeResult.Success)
                    {
                        result.SuccessfulCharges++;
                        result.TotalAmountCharged += chargeResult.AmountCharged;
                    }
                    else if (chargeResult.SkippedReason != null)
                    {
                        result.SkippedInvoices++;
                    }
                    else
                    {
                        result.FailedCharges++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Auto-billing failed for invoice {InvoiceId}.", invoice.Id);
                    result.FailedCharges++;
                }
            }

            return result;
        }

        public async Task<AutoBillingChargeResult> ChargeInvoiceAsync(int invoiceId, CancellationToken cancellationToken = default)
        {
            var invoice = await _db.Invoices
                .Include(i => i.MemberSubscription)
                .ThenInclude(s => s!.SubscriptionPlan)
                .FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);

            if (invoice is null)
            {
                return AutoBillingChargeResult.Skipped("Invoice not found.");
            }

            if (invoice.Status == InvoiceStatus.Paid || invoice.Status == InvoiceStatus.Voided)
            {
                return AutoBillingChargeResult.Skipped("Invoice already paid or voided.");
            }

            if (string.IsNullOrWhiteSpace(invoice.MemberUserId))
            {
                return AutoBillingChargeResult.Skipped("Invoice has no member associated.");
            }

            // Check for recent failed attempts to avoid hammering
            var recentAttemptCutoff = DateTime.UtcNow.AddHours(-24);
            var recentFailedAttempts = await _db.AutoBillingAttempts
                .CountAsync(a =>
                    a.InvoiceId == invoiceId &&
                    !a.Succeeded &&
                    a.AttemptedAtUtc >= recentAttemptCutoff,
                    cancellationToken);

            if (recentFailedAttempts >= 3)
            {
                return AutoBillingChargeResult.Skipped("Too many recent failed attempts.");
            }

            // Get member's default payment method
            var savedPaymentMethod = await GetDefaultPaymentMethodAsync(invoice.MemberUserId, cancellationToken);
            
            if (savedPaymentMethod is null)
            {
                return AutoBillingChargeResult.Skipped("No saved payment method.");
            }

            if (!savedPaymentMethod.AutoBillingEnabled)
            {
                return AutoBillingChargeResult.Skipped("Auto-billing disabled for this payment method.");
            }

            if (string.Equals(savedPaymentMethod.GatewayProvider, "PayMongo", StringComparison.OrdinalIgnoreCase) &&
                !PayMongoBillingCapabilities.SupportsOffSessionAutoBilling)
            {
                savedPaymentMethod.AutoBillingEnabled = false;
                await _db.SaveChangesAsync(cancellationToken);

                if (_outbox is not null)
                {
                    await _outbox.EnqueueUserAsync(
                        invoice.MemberUserId,
                        eventType: "billing.auto.unavailable",
                        message: PayMongoBillingCapabilities.ManualRenewalMessage,
                        data: new
                        {
                            invoiceId = invoice.Id,
                            invoiceNumber = invoice.InvoiceNumber,
                            gatewayProvider = savedPaymentMethod.GatewayProvider
                        },
                        cancellationToken: cancellationToken);
                }

                _logger.LogInformation(
                    "Disabled auto-billing for invoice {InvoiceId}, member {MemberUserId}: {Reason}",
                    invoiceId,
                    invoice.MemberUserId,
                    PayMongoBillingCapabilities.UnsupportedAutoBillingReason);

                return AutoBillingChargeResult.Skipped(PayMongoBillingCapabilities.UnsupportedAutoBillingReason);
            }

            if (savedPaymentMethod.FailedAttempts >= MaxFailedAttempts)
            {
                return AutoBillingChargeResult.Skipped("Payment method disabled due to repeated failures.");
            }

            var nowUtc = DateTime.UtcNow;
            var planName = invoice.MemberSubscription?.SubscriptionPlan?.Name ?? "Gym Subscription";

            // Create the billing attempt record
            var attempt = new AutoBillingAttempt
            {
                InvoiceId = invoiceId,
                SavedPaymentMethodId = savedPaymentMethod.Id,
                AttemptedAtUtc = nowUtc,
                Amount = invoice.Amount,
                Succeeded = false
            };
            _db.AutoBillingAttempts.Add(attempt);
            await _db.SaveChangesAsync(cancellationToken);

            try
            {
                // Attempt to charge via PayMongo
                var chargeResult = await _payMongoClient.CreatePaymentIntentAsync(
                    invoice.Amount,
                    "PHP",
                    savedPaymentMethod.GatewayPaymentMethodId,
                    description: $"Auto-renewal: {planName} (Invoice #{invoice.InvoiceNumber})",
                    metadata: new Dictionary<string, string>
                    {
                        ["invoice_id"] = invoice.Id.ToString(),
                        ["invoice_number"] = invoice.InvoiceNumber,
                        ["member_user_id"] = invoice.MemberUserId,
                        ["auto_billing"] = "true"
                    },
                    cancellationToken);

                attempt.GatewayPaymentIntentId = chargeResult.PaymentIntentId;
                attempt.GatewayStatus = chargeResult.Status;

                if (chargeResult.IsSuccessful)
                {
                    // Payment succeeded! Create payment record and update invoice
                    var payment = new Payment
                    {
                        InvoiceId = invoiceId,
                        BranchId = invoice.BranchId,
                        Amount = invoice.Amount,
                        Method = PaymentMethod.OnlineGateway,
                        Status = PaymentStatus.Succeeded,
                        PaidAtUtc = nowUtc,
                        GatewayProvider = "PayMongo",
                        GatewayPaymentId = chargeResult.PaymentIntentId,
                        ReferenceNumber = $"AUTO-{chargeResult.PaymentIntentId}"
                    };
                    _db.Payments.Add(payment);

                    invoice.Status = InvoiceStatus.Paid;

                    attempt.Succeeded = true;
                    attempt.PaymentId = payment.Id;

                    // Update payment method usage
                    savedPaymentMethod.LastUsedUtc = nowUtc;
                    savedPaymentMethod.FailedAttempts = 0;

                    await _db.SaveChangesAsync(cancellationToken);

                    // Queue notification
                    if (_outbox is not null)
                    {
                        await _outbox.EnqueueUserAsync(
                            invoice.MemberUserId,
                            eventType: "billing.auto.succeeded",
                            message: $"Auto-payment of ₱{invoice.Amount:N2} successful for {planName}.",
                            data: new
                            {
                                invoiceId = invoice.Id,
                                invoiceNumber = invoice.InvoiceNumber,
                                amount = invoice.Amount,
                                planName,
                                paidAtUtc = nowUtc
                            },
                            cancellationToken: cancellationToken);
                    }

                    _logger.LogInformation(
                        "Auto-billing succeeded for invoice {InvoiceId}, member {MemberUserId}, amount {Amount}.",
                        invoiceId, invoice.MemberUserId, invoice.Amount);

                    return AutoBillingChargeResult.Successful(invoice.Amount, payment.Id);
                }
                else if (chargeResult.RequiresAction)
                {
                    // 3DS required - can't auto-charge, need user interaction
                    attempt.ErrorMessage = "Payment requires 3D Secure authentication.";
                    await _db.SaveChangesAsync(cancellationToken);

                    _logger.LogWarning(
                        "Auto-billing requires 3DS for invoice {InvoiceId}. Manual payment needed.",
                        invoiceId);

                    // Notify member they need to complete payment manually
                    if (_outbox is not null)
                    {
                        await _outbox.EnqueueUserAsync(
                            invoice.MemberUserId,
                            eventType: "billing.auto.requires_action",
                            message: $"Your subscription renewal of ₱{invoice.Amount:N2} requires verification. Please complete the payment manually.",
                            data: new
                            {
                                invoiceId = invoice.Id,
                                invoiceNumber = invoice.InvoiceNumber,
                                amount = invoice.Amount,
                                reason = "3D Secure verification required"
                            },
                            cancellationToken: cancellationToken);
                    }

                    return AutoBillingChargeResult.Failed("3D Secure authentication required.");
                }
                else
                {
                    // Payment failed
                    attempt.ErrorMessage = chargeResult.ErrorMessage ?? "Payment failed.";
                    savedPaymentMethod.FailedAttempts++;
                    savedPaymentMethod.LastFailedAtUtc = nowUtc;

                    // Disable payment method if too many failures
                    if (savedPaymentMethod.FailedAttempts >= MaxFailedAttempts)
                    {
                        savedPaymentMethod.IsActive = false;
                        savedPaymentMethod.AutoBillingEnabled = false;
                    }

                    await _db.SaveChangesAsync(cancellationToken);

                    _logger.LogWarning(
                        "Auto-billing failed for invoice {InvoiceId}: {Error}",
                        invoiceId, chargeResult.ErrorMessage);

                    // Notify member of failure
                    if (_outbox is not null)
                    {
                        await _outbox.EnqueueUserAsync(
                            invoice.MemberUserId,
                            eventType: "billing.auto.failed",
                            message: $"Auto-payment failed for {planName}. Please update your payment method or pay manually.",
                            data: new
                            {
                                invoiceId = invoice.Id,
                                invoiceNumber = invoice.InvoiceNumber,
                                amount = invoice.Amount,
                                failedAttempts = savedPaymentMethod.FailedAttempts,
                                error = chargeResult.ErrorMessage
                            },
                            cancellationToken: cancellationToken);
                    }

                    return AutoBillingChargeResult.Failed(chargeResult.ErrorMessage ?? "Payment declined.");
                }
            }
            catch (Exception ex)
            {
                attempt.ErrorMessage = ex.Message;
                savedPaymentMethod.FailedAttempts++;
                savedPaymentMethod.LastFailedAtUtc = nowUtc;
                await _db.SaveChangesAsync(cancellationToken);

                _logger.LogError(ex, "Auto-billing exception for invoice {InvoiceId}.", invoiceId);
                throw;
            }
        }

        public async Task<SavedPaymentMethod?> GetDefaultPaymentMethodAsync(string memberUserId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(memberUserId))
            {
                return null;
            }

            return await _db.SavedPaymentMethods
                .Where(m =>
                    m.MemberUserId == memberUserId &&
                    m.IsActive &&
                    m.GatewayProvider == "PayMongo")
                .OrderByDescending(m => m.IsDefault)
                .ThenByDescending(m => m.LastUsedUtc ?? m.CreatedUtc)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<SavedPaymentMethod> SavePaymentMethodAsync(
            string memberUserId,
            string gatewayPaymentMethodId,
            string paymentMethodType,
            string? displayLabel = null,
            string? customerId = null,
            bool enableAutoBilling = true,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(memberUserId))
            {
                throw new ArgumentException("Member user ID is required.", nameof(memberUserId));
            }

            if (enableAutoBilling && !PayMongoBillingCapabilities.SupportsOffSessionAutoBilling)
            {
                enableAutoBilling = false;
            }

            // Deactivate existing default payment methods
            var existingMethods = await _db.SavedPaymentMethods
                .Where(m => m.MemberUserId == memberUserId && m.IsDefault)
                .ToListAsync(cancellationToken);

            foreach (var existing in existingMethods)
            {
                existing.IsDefault = false;
            }

            var savedMethod = new SavedPaymentMethod
            {
                MemberUserId = memberUserId,
                GatewayProvider = "PayMongo",
                GatewayCustomerId = customerId,
                GatewayPaymentMethodId = gatewayPaymentMethodId,
                PaymentMethodType = paymentMethodType,
                DisplayLabel = displayLabel,
                IsDefault = true,
                AutoBillingEnabled = enableAutoBilling,
                CreatedUtc = DateTime.UtcNow
            };

            _db.SavedPaymentMethods.Add(savedMethod);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Saved payment method {PaymentMethodId} for member {MemberUserId}. Auto-billing: {AutoBilling}",
                gatewayPaymentMethodId, memberUserId, enableAutoBilling);

            return savedMethod;
        }

        public async Task DisableAutoBillingAsync(string memberUserId, CancellationToken cancellationToken = default)
        {
            var methods = await _db.SavedPaymentMethods
                .Where(m => m.MemberUserId == memberUserId && m.AutoBillingEnabled)
                .ToListAsync(cancellationToken);

            foreach (var method in methods)
            {
                method.AutoBillingEnabled = false;
            }

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Disabled auto-billing for member {MemberUserId}.", memberUserId);
        }
    }

    public class AutoBillingRunResult
    {
        public DateTime ProcessedAtUtc { get; set; }
        public int TotalDueInvoices { get; set; }
        public int SuccessfulCharges { get; set; }
        public int FailedCharges { get; set; }
        public int SkippedInvoices { get; set; }
        public decimal TotalAmountCharged { get; set; }
    }

    public class AutoBillingChargeResult
    {
        public bool Success { get; private set; }
        public decimal AmountCharged { get; private set; }
        public int? PaymentId { get; private set; }
        public string? ErrorMessage { get; private set; }
        public string? SkippedReason { get; private set; }

        public static AutoBillingChargeResult Successful(decimal amount, int paymentId) =>
            new() { Success = true, AmountCharged = amount, PaymentId = paymentId };

        public static AutoBillingChargeResult Failed(string errorMessage) =>
            new() { Success = false, ErrorMessage = errorMessage };

        public static AutoBillingChargeResult Skipped(string reason) =>
            new() { Success = false, SkippedReason = reason };
    }
}
