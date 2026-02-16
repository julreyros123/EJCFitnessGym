using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Models.Integration;
using EJCFitnessGym.Services.Finance;
using EJCFitnessGym.Services.Integration;
using EJCFitnessGym.Services.Memberships;
using EJCFitnessGym.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EJCFitnessGym.Controllers
{
    [ApiController]
    [Route("api/webhooks/paymongo")]
    [AllowAnonymous]
    public class PayMongoWebhookController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IMembershipService _membershipService;
        private readonly PayMongoOptions _payMongoOptions;
        private readonly ILogger<PayMongoWebhookController> _logger;
        private readonly IIntegrationOutbox _outbox;
        private readonly IFinanceAlertService _financeAlertService;

        private static readonly Regex PlanTokenRegex = new(@"\[plan:(\d+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly HashSet<string> PaidEventTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "checkout_session.payment.paid"
        };

        private static readonly HashSet<string> FailedEventTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "checkout_session.payment.failed",
            "checkout_session.expired"
        };

        public PayMongoWebhookController(
            ApplicationDbContext db,
            IMembershipService membershipService,
            IFinanceAlertService financeAlertService,
            IIntegrationOutbox outbox,
            IOptions<PayMongoOptions> payMongoOptions,
            ILogger<PayMongoWebhookController> logger)
        {
            _db = db;
            _membershipService = membershipService;
            _financeAlertService = financeAlertService;
            _outbox = outbox;
            _payMongoOptions = payMongoOptions.Value;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> Receive(CancellationToken cancellationToken)
        {
            var rawBody = await ReadRequestBodyAsync();
            if (string.IsNullOrWhiteSpace(rawBody))
            {
                return BadRequest();
            }

            if (!VerifyWebhookSignature(rawBody))
            {
                return Unauthorized();
            }

            JsonDocument jsonDocument;
            try
            {
                jsonDocument = JsonDocument.Parse(rawBody);
            }
            catch (JsonException)
            {
                return BadRequest();
            }

            using (jsonDocument)
            {
                var payload = jsonDocument.RootElement;
                if (!payload.TryGetProperty("data", out var data) ||
                    !data.TryGetProperty("attributes", out var eventAttributes))
                {
                    return BadRequest();
                }

                var eventType = TryGetString(eventAttributes, "type");
                if (string.IsNullOrWhiteSpace(eventType))
                {
                    _logger.LogWarning("PayMongo webhook payload did not include event type.");
                    return Ok();
                }

                if (!PaidEventTypes.Contains(eventType) && !FailedEventTypes.Contains(eventType))
                {
                    return Ok();
                }

                if (!eventAttributes.TryGetProperty("data", out var checkoutSession))
                {
                    _logger.LogWarning("PayMongo webhook payload did not include checkout session details.");
                    return Ok();
                }

                var checkoutSessionId = TryGetString(checkoutSession, "id");
                if (string.IsNullOrWhiteSpace(checkoutSessionId))
                {
                    _logger.LogWarning("PayMongo webhook payload did not include checkout session id.");
                    return Ok();
                }

                var payMongoPaymentId = TryGetPayMongoPaymentId(checkoutSession);
                var eventKey = BuildWebhookEventKey(payload, eventType, checkoutSessionId, payMongoPaymentId);
                var receipt = await BeginWebhookProcessingAsync(
                    provider: "PayMongo",
                    eventKey: eventKey,
                    eventType: eventType,
                    externalReference: checkoutSessionId,
                    cancellationToken);

                if (receipt is null)
                {
                    return Ok();
                }

                try
                {
                    var payment = await _db.Payments
                        .Include(p => p.Invoice)
                        .FirstOrDefaultAsync(p => p.ReferenceNumber == checkoutSessionId, cancellationToken);

                    if (payment is null || payment.Invoice is null)
                    {
                        _logger.LogWarning("PayMongo webhook checkout session {CheckoutSessionId} was not matched to an invoice payment.", checkoutSessionId);
                        await CompleteWebhookProcessingAsync(receipt, "Ignored", "No internal payment matched the checkout session.", cancellationToken);
                        return Ok();
                    }

                    await _membershipService.RunLifecycleMaintenanceAsync(cancellationToken: cancellationToken);

                    if (PaidEventTypes.Contains(eventType))
                    {
                        await HandlePaidCheckoutEventAsync(payment, checkoutSession, checkoutSessionId, cancellationToken);
                        await CompleteWebhookProcessingAsync(receipt, "Processed", null, cancellationToken);
                        _ = await _financeAlertService.EvaluateAndNotifyAsync(
                            "paymongo.payment.succeeded",
                            cancellationToken);
                        return Ok();
                    }

                    if (FailedEventTypes.Contains(eventType))
                    {
                        await HandleFailedCheckoutEventAsync(payment, checkoutSession, checkoutSessionId, eventType, cancellationToken);
                        await CompleteWebhookProcessingAsync(receipt, "Processed", null, cancellationToken);
                        return Ok();
                    }

                    await CompleteWebhookProcessingAsync(receipt, "Ignored", "Event type is not handled.", cancellationToken);
                    return Ok();
                }
                catch (Exception ex)
                {
                    await MarkWebhookAsFailedAsync(receipt.Id, ex.Message, cancellationToken);
                    _logger.LogError(ex, "PayMongo webhook processing failed for checkout session {CheckoutSessionId}.", checkoutSessionId);
                    return StatusCode(StatusCodes.Status500InternalServerError);
                }
            }
        }

        private async Task<InboundWebhookReceipt?> BeginWebhookProcessingAsync(
            string provider,
            string eventKey,
            string? eventType,
            string? externalReference,
            CancellationToken cancellationToken)
        {
            var nowUtc = DateTime.UtcNow;
            var receipt = await _db.InboundWebhookReceipts
                .FirstOrDefaultAsync(
                    r => r.Provider == provider && r.EventKey == eventKey,
                    cancellationToken);

            if (receipt is null)
            {
                receipt = new InboundWebhookReceipt
                {
                    Provider = provider,
                    EventKey = eventKey,
                    EventType = eventType,
                    ExternalReference = externalReference,
                    Status = "Processing",
                    AttemptCount = 1,
                    FirstReceivedUtc = nowUtc,
                    LastAttemptUtc = nowUtc,
                    CreatedUtc = nowUtc,
                    UpdatedUtc = nowUtc
                };

                _db.InboundWebhookReceipts.Add(receipt);
                await _db.SaveChangesAsync(cancellationToken);
                return receipt;
            }

            if (string.Equals(receipt.Status, "Processed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(receipt.Status, "Ignored", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "Ignoring duplicate webhook event {Provider}:{EventKey}; already marked as {Status}.",
                    provider,
                    eventKey,
                    receipt.Status);
                return null;
            }

            if (string.Equals(receipt.Status, "Processing", StringComparison.OrdinalIgnoreCase) &&
                nowUtc - receipt.UpdatedUtc < TimeSpan.FromMinutes(2))
            {
                _logger.LogInformation(
                    "Ignoring concurrent webhook processing attempt for event {Provider}:{EventKey}.",
                    provider,
                    eventKey);
                return null;
            }

            receipt.AttemptCount++;
            receipt.Status = "Processing";
            receipt.LastAttemptUtc = nowUtc;
            receipt.EventType = string.IsNullOrWhiteSpace(eventType) ? receipt.EventType : eventType;
            if (!string.IsNullOrWhiteSpace(externalReference))
            {
                receipt.ExternalReference = externalReference;
            }

            receipt.Notes = null;
            receipt.UpdatedUtc = nowUtc;
            await _db.SaveChangesAsync(cancellationToken);
            return receipt;
        }

        private async Task CompleteWebhookProcessingAsync(
            InboundWebhookReceipt receipt,
            string status,
            string? notes,
            CancellationToken cancellationToken)
        {
            var nowUtc = DateTime.UtcNow;
            receipt.Status = status;
            receipt.Notes = string.IsNullOrWhiteSpace(notes)
                ? null
                : (notes.Length <= 2000 ? notes : notes[..2000]);
            receipt.UpdatedUtc = nowUtc;

            if (string.Equals(status, "Processed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "Ignored", StringComparison.OrdinalIgnoreCase))
            {
                receipt.ProcessedUtc = nowUtc;
            }

            await _db.SaveChangesAsync(cancellationToken);
        }

        private async Task MarkWebhookAsFailedAsync(
            int receiptId,
            string? notes,
            CancellationToken cancellationToken)
        {
            _db.ChangeTracker.Clear();

            var receipt = await _db.InboundWebhookReceipts.FirstOrDefaultAsync(r => r.Id == receiptId, cancellationToken);
            if (receipt is null)
            {
                return;
            }

            await CompleteWebhookProcessingAsync(receipt, "Failed", notes, cancellationToken);
        }

        private static string BuildWebhookEventKey(
            JsonElement payload,
            string eventType,
            string checkoutSessionId,
            string? payMongoPaymentId)
        {
            if (payload.TryGetProperty("data", out var data))
            {
                var eventId = TryGetString(data, "id");
                if (!string.IsNullOrWhiteSpace(eventId))
                {
                    return eventId.Trim();
                }
            }

            if (!string.IsNullOrWhiteSpace(payMongoPaymentId))
            {
                return $"{eventType}:{checkoutSessionId}:{payMongoPaymentId.Trim()}";
            }

            return $"{eventType}:{checkoutSessionId}";
        }

        private async Task HandlePaidCheckoutEventAsync(
            Payment payment,
            JsonElement checkoutSession,
            string checkoutSessionId,
            CancellationToken cancellationToken)
        {
            if (payment.Invoice is null)
            {
                return;
            }

            var payMongoPaymentId = TryGetPayMongoPaymentId(checkoutSession);
            if (payment.Status == PaymentStatus.Succeeded &&
                payment.Invoice.Status == InvoiceStatus.Paid &&
                string.Equals(payment.ReferenceNumber, checkoutSessionId, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(payMongoPaymentId) ||
                 string.Equals(payment.GatewayPaymentId, payMongoPaymentId, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogInformation(
                    "Ignoring duplicate PayMongo paid webhook for checkout session {CheckoutSessionId}.",
                    checkoutSessionId);
                return;
            }

            var resolvedMemberUserId = !string.IsNullOrWhiteSpace(payment.Invoice.MemberUserId)
                ? payment.Invoice.MemberUserId
                : TryGetMetadataValue(checkoutSession, "member_user_id");

            var resolvedPlanId = TryGetMetadataInt(checkoutSession, "plan_id");
            if (!resolvedPlanId.HasValue && payment.Invoice.MemberSubscriptionId.HasValue)
            {
                resolvedPlanId = await _db.MemberSubscriptions
                    .AsNoTracking()
                    .Where(s => s.Id == payment.Invoice.MemberSubscriptionId.Value)
                    .Select(s => (int?)s.SubscriptionPlanId)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            resolvedPlanId ??= ExtractPlanIdFromInvoiceNotes(payment.Invoice.Notes);

            var resolvedPlanName = TryGetMetadataValue(checkoutSession, "plan_name");
            var resolvedExternalCustomerId = TryGetMetadataValue(checkoutSession, "customer_id");
            var resolvedPaidAmount = TryResolvePaidAmount(checkoutSession);
            var amountMismatch = resolvedPaidAmount.HasValue && Math.Abs(resolvedPaidAmount.Value - payment.Amount) > 0.50m;

            int? activatedSubscriptionId = null;
            string? reconciliationWarning = null;

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            payment.Method = PaymentMethod.OnlineGateway;
            payment.Status = PaymentStatus.Succeeded;
            payment.GatewayProvider = "PayMongo";
            payment.GatewayPaymentId = payMongoPaymentId;
            payment.PaidAtUtc = DateTime.UtcNow;
            payment.ReferenceNumber = string.IsNullOrWhiteSpace(payment.ReferenceNumber)
                ? checkoutSessionId
                : payment.ReferenceNumber;
            payment.Invoice.Status = InvoiceStatus.Paid;

            if (amountMismatch)
            {
                reconciliationWarning =
                    $"Paid amount mismatch. Expected {payment.Amount:N2} PHP, webhook amount {resolvedPaidAmount!.Value:N2} PHP.";
            }
            else if (string.IsNullOrWhiteSpace(resolvedMemberUserId) || !resolvedPlanId.HasValue)
            {
                reconciliationWarning =
                    "Could not auto-activate membership because member or plan metadata was missing.";
            }
            else
            {
                var plan = await _db.SubscriptionPlans
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == resolvedPlanId.Value, cancellationToken);

                if (plan is null)
                {
                    reconciliationWarning = $"Resolved plan id {resolvedPlanId.Value} does not exist.";
                }
                else
                {
                    var subscription = await _membershipService.ActivateSubscriptionAsync(
                        resolvedMemberUserId,
                        plan.Id,
                        externalSubscriptionId: checkoutSessionId,
                        externalCustomerId: resolvedExternalCustomerId,
                        cancellationToken: cancellationToken);

                    payment.Invoice.MemberSubscription = subscription;
                    resolvedPlanName ??= plan.Name;
                    activatedSubscriptionId = subscription.Id;
                }
            }

            var resolvedTargetUserId = string.IsNullOrWhiteSpace(resolvedMemberUserId)
                ? payment.Invoice.MemberUserId
                : resolvedMemberUserId;

            var realtimePayload = new
            {
                paymentId = payment.Id,
                invoiceId = payment.InvoiceId,
                memberUserId = resolvedTargetUserId,
                amount = payment.Amount,
                webhookAmount = resolvedPaidAmount,
                amountMismatch,
                gatewayProvider = payment.GatewayProvider,
                checkoutSessionId,
                gatewayPaymentId = payment.GatewayPaymentId,
                planId = resolvedPlanId,
                planName = resolvedPlanName,
                subscriptionId = activatedSubscriptionId,
                reconciliationWarning
            };

            await _outbox.EnqueueBackOfficeAsync(
                "payment.succeeded",
                "Membership payment confirmed via PayMongo.",
                realtimePayload,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(resolvedTargetUserId))
            {
                await _outbox.EnqueueUserAsync(
                    resolvedTargetUserId,
                    "payment.succeeded",
                    "Your payment was confirmed successfully.",
                    realtimePayload,
                    cancellationToken);

                if (activatedSubscriptionId.HasValue)
                {
                    await _outbox.EnqueueUserAsync(
                        resolvedTargetUserId,
                        "membership.activated",
                        "Your membership is now active.",
                        realtimePayload,
                        cancellationToken);
                }
            }

            if (!string.IsNullOrWhiteSpace(reconciliationWarning))
            {
                _logger.LogWarning(
                    "PayMongo reconciliation warning for checkout session {CheckoutSessionId}: {Warning}",
                    checkoutSessionId,
                    reconciliationWarning);

                await _outbox.EnqueueBackOfficeAsync(
                    "membership.reconciliation.warning",
                    reconciliationWarning,
                    realtimePayload,
                    cancellationToken);
            }

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        private async Task HandleFailedCheckoutEventAsync(
            Payment payment,
            JsonElement checkoutSession,
            string checkoutSessionId,
            string eventType,
            CancellationToken cancellationToken)
        {
            if (payment.Invoice is null)
            {
                return;
            }

            if (payment.Status == PaymentStatus.Succeeded)
            {
                _logger.LogWarning(
                    "Ignoring PayMongo failed webhook for checkout session {CheckoutSessionId} because payment is already marked succeeded.",
                    checkoutSessionId);
                return;
            }

            var payMongoPaymentId = TryGetPayMongoPaymentId(checkoutSession);
            var nowUtc = DateTime.UtcNow;

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            payment.Method = PaymentMethod.OnlineGateway;
            payment.Status = PaymentStatus.Failed;
            payment.GatewayProvider = "PayMongo";
            payment.GatewayPaymentId = string.IsNullOrWhiteSpace(payMongoPaymentId) ? payment.GatewayPaymentId : payMongoPaymentId;
            payment.PaidAtUtc = nowUtc;
            payment.ReferenceNumber = string.IsNullOrWhiteSpace(payment.ReferenceNumber)
                ? checkoutSessionId
                : payment.ReferenceNumber;
            payment.Invoice.Status = payment.Invoice.DueDateUtc < nowUtc
                ? InvoiceStatus.Overdue
                : InvoiceStatus.Unpaid;

            var realtimePayload = new
            {
                paymentId = payment.Id,
                invoiceId = payment.InvoiceId,
                memberUserId = payment.Invoice.MemberUserId,
                amount = payment.Amount,
                gatewayProvider = payment.GatewayProvider,
                checkoutSessionId,
                gatewayPaymentId = payment.GatewayPaymentId,
                invoiceStatus = payment.Invoice.Status.ToString(),
                eventType
            };

            await _outbox.EnqueueBackOfficeAsync(
                "payment.failed",
                "PayMongo checkout did not complete successfully.",
                realtimePayload,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(payment.Invoice.MemberUserId))
            {
                await _outbox.EnqueueUserAsync(
                    payment.Invoice.MemberUserId,
                    "payment.failed",
                    "Your payment was not completed. Please retry to keep membership active.",
                    realtimePayload,
                    cancellationToken);
            }

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        private bool VerifyWebhookSignature(string rawBody)
        {
            var webhookSecret = _payMongoOptions.WebhookSecret;
            if (string.IsNullOrWhiteSpace(webhookSecret))
            {
                if (_payMongoOptions.RequireWebhookSignature)
                {
                    _logger.LogError("PayMongo webhook secret is required but not configured.");
                    return false;
                }

                return true;
            }

            if (!Request.Headers.TryGetValue("Paymongo-Signature", out var signatureHeaderValues) &&
                !Request.Headers.TryGetValue("PayMongo-Signature", out signatureHeaderValues))
            {
                _logger.LogWarning("PayMongo webhook signature header was missing.");
                return false;
            }

            if (!TryParseSignatureHeader(signatureHeaderValues.ToString(), out var timestampUnix, out var testSignature, out var liveSignature))
            {
                _logger.LogWarning("PayMongo webhook signature header format was invalid.");
                return false;
            }

            var toleranceSeconds = Math.Max(0, _payMongoOptions.WebhookSignatureToleranceSeconds);
            if (toleranceSeconds > 0)
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (Math.Abs(now - timestampUnix) > toleranceSeconds)
                {
                    _logger.LogWarning("PayMongo webhook signature timestamp is outside tolerance.");
                    return false;
                }
            }

            var computedSignature = ComputeWebhookSignature(webhookSecret, timestampUnix, rawBody);
            var matches = false;

            if (!string.IsNullOrWhiteSpace(testSignature))
            {
                matches = FixedTimeEquals(computedSignature, testSignature);
            }

            if (!matches && !string.IsNullOrWhiteSpace(liveSignature))
            {
                matches = FixedTimeEquals(computedSignature, liveSignature);
            }

            if (!matches)
            {
                _logger.LogWarning("PayMongo webhook signature verification failed.");
            }

            return matches;
        }

        private static bool TryParseSignatureHeader(string headerValue, out long timestampUnix, out string? testSignature, out string? liveSignature)
        {
            timestampUnix = 0;
            testSignature = null;
            liveSignature = null;

            if (string.IsNullOrWhiteSpace(headerValue))
            {
                return false;
            }

            var pairs = headerValue.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                var index = pair.IndexOf('=');
                if (index <= 0 || index >= pair.Length - 1)
                {
                    continue;
                }

                var key = pair[..index].Trim();
                var value = pair[(index + 1)..].Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (key.Equals("t", StringComparison.OrdinalIgnoreCase))
                {
                    _ = long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out timestampUnix);
                }
                else if (key.Equals("te", StringComparison.OrdinalIgnoreCase))
                {
                    testSignature = value;
                }
                else if (key.Equals("li", StringComparison.OrdinalIgnoreCase))
                {
                    liveSignature = value;
                }
            }

            return timestampUnix > 0 && (!string.IsNullOrWhiteSpace(testSignature) || !string.IsNullOrWhiteSpace(liveSignature));
        }

        private static string ComputeWebhookSignature(string webhookSecret, long timestampUnix, string rawBody)
        {
            var signedPayload = $"{timestampUnix}.{rawBody}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(webhookSecret));
            var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        private static bool FixedTimeEquals(string leftHex, string rightHex)
        {
            var left = Encoding.UTF8.GetBytes(leftHex.ToLowerInvariant());
            var right = Encoding.UTF8.GetBytes(rightHex.ToLowerInvariant());
            return left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);
        }

        private async Task<string> ReadRequestBodyAsync()
        {
            Request.EnableBuffering();
            Request.Body.Position = 0;

            using var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            var body = await reader.ReadToEndAsync();

            Request.Body.Position = 0;
            return body;
        }

        private static string? TryGetPayMongoPaymentId(JsonElement checkoutSession)
        {
            if (!checkoutSession.TryGetProperty("attributes", out var sessionAttributes) ||
                !sessionAttributes.TryGetProperty("payments", out var paymentsArray) ||
                paymentsArray.ValueKind != JsonValueKind.Array ||
                paymentsArray.GetArrayLength() == 0)
            {
                return null;
            }

            var firstPayment = paymentsArray[0];
            return TryGetString(firstPayment, "id");
        }

        private static decimal? TryResolvePaidAmount(JsonElement checkoutSession)
        {
            if (!checkoutSession.TryGetProperty("attributes", out var sessionAttributes))
            {
                return null;
            }

            if (sessionAttributes.TryGetProperty("payments", out var paymentsArray) &&
                paymentsArray.ValueKind == JsonValueKind.Array &&
                paymentsArray.GetArrayLength() > 0)
            {
                var firstPayment = paymentsArray[0];
                if (TryGetMinorUnitAmount(firstPayment, "amount", out var paymentAmount))
                {
                    return paymentAmount;
                }

                if (firstPayment.TryGetProperty("attributes", out var paymentAttributes) &&
                    TryGetMinorUnitAmount(paymentAttributes, "amount", out paymentAmount))
                {
                    return paymentAmount;
                }
            }

            if (TryGetMinorUnitAmount(sessionAttributes, "amount_total", out var totalAmount))
            {
                return totalAmount;
            }

            if (TryGetMinorUnitAmount(sessionAttributes, "amount", out var amount))
            {
                return amount;
            }

            return null;
        }

        private static bool TryGetMinorUnitAmount(JsonElement container, string propertyName, out decimal amount)
        {
            amount = 0m;
            if (!container.TryGetProperty(propertyName, out var rawValue))
            {
                return false;
            }

            decimal parsed;
            if (rawValue.ValueKind == JsonValueKind.Number)
            {
                parsed = rawValue.GetDecimal();
            }
            else if (rawValue.ValueKind == JsonValueKind.String &&
                     decimal.TryParse(rawValue.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var fromString))
            {
                parsed = fromString;
            }
            else
            {
                return false;
            }

            amount = parsed / 100m;
            return true;
        }

        private static string? TryGetMetadataValue(JsonElement checkoutSession, string key)
        {
            if (!checkoutSession.TryGetProperty("attributes", out var sessionAttributes) ||
                !sessionAttributes.TryGetProperty("metadata", out var metadata) ||
                metadata.ValueKind != JsonValueKind.Object ||
                !metadata.TryGetProperty(key, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                _ => value.ToString()
            };
        }

        private static int? TryGetMetadataInt(JsonElement checkoutSession, string key)
        {
            var value = TryGetMetadataValue(checkoutSession, key);
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }

        private static int? ExtractPlanIdFromInvoiceNotes(string? notes)
        {
            if (string.IsNullOrWhiteSpace(notes))
            {
                return null;
            }

            var match = PlanTokenRegex.Match(notes);
            if (!match.Success)
            {
                return null;
            }

            return int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }

        private static string? TryGetString(JsonElement container, string propertyName)
        {
            if (!container.TryGetProperty(propertyName, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            };
        }
    }
}
