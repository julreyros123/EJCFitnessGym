using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Models.Integration;
using EJCFitnessGym.Services.Integration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Controllers
{
    [ApiController]
    [Authorize(Roles = "Finance,Admin,SuperAdmin")]
    [Route("api/admin/integration")]
    public class IntegrationOpsController : ControllerBase
    {
        private static readonly HashSet<string> PaidEventTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "checkout_session.payment.paid"
        };

        private static readonly HashSet<string> FailedEventTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "checkout_session.payment.failed",
            "checkout_session.expired"
        };

        private readonly ApplicationDbContext _db;
        private readonly IIntegrationOutbox _outbox;

        public IntegrationOpsController(
            ApplicationDbContext db,
            IIntegrationOutbox outbox)
        {
            _db = db;
            _outbox = outbox;
        }

        [HttpGet("outbox")]
        public async Task<IActionResult> GetOutboxMessages(
            [FromQuery] IntegrationOutboxStatus? status = null,
            [FromQuery] int take = 100,
            CancellationToken cancellationToken = default)
        {
            take = Math.Clamp(take, 1, 500);

            IQueryable<IntegrationOutboxMessage> query = _db.IntegrationOutboxMessages.AsNoTracking();
            if (status.HasValue)
            {
                query = query.Where(m => m.Status == status.Value);
            }

            var messages = await query
                .OrderBy(m => m.Status)
                .ThenBy(m => m.NextAttemptUtc)
                .ThenBy(m => m.Id)
                .Take(take)
                .ToListAsync(cancellationToken);

            return Ok(messages.Select(m => new
            {
                id = m.Id,
                target = m.Target.ToString(),
                targetValue = m.TargetValue,
                eventType = m.EventType,
                message = m.Message,
                status = m.Status.ToString(),
                attemptCount = m.AttemptCount,
                lastError = m.LastError,
                nextAttemptUtc = m.NextAttemptUtc,
                lastAttemptUtc = m.LastAttemptUtc,
                processedUtc = m.ProcessedUtc,
                createdUtc = m.CreatedUtc,
                updatedUtc = m.UpdatedUtc
            }));
        }

        [HttpPost("outbox/{id:int}/retry")]
        public async Task<IActionResult> RetryOutboxMessage(
            int id,
            CancellationToken cancellationToken = default)
        {
            var message = await _db.IntegrationOutboxMessages
                .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

            if (message is null)
            {
                return NotFound(new { message = "Outbox message not found." });
            }

            if (message.Status == IntegrationOutboxStatus.Processed)
            {
                return BadRequest(new { message = "Cannot retry a processed outbox message." });
            }

            message.Status = IntegrationOutboxStatus.Pending;
            message.NextAttemptUtc = DateTime.UtcNow;
            message.LastError = null;
            message.UpdatedUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                id = message.Id,
                status = message.Status.ToString(),
                nextAttemptUtc = message.NextAttemptUtc
            });
        }

        [HttpPost("outbox/retry-failed")]
        public async Task<IActionResult> RetryFailedOutboxMessages(
            [FromBody] RetryFailedOutboxRequest? request,
            CancellationToken cancellationToken = default)
        {
            var take = Math.Clamp(request?.Take ?? 50, 1, 500);
            var nowUtc = DateTime.UtcNow;

            var failedMessages = await _db.IntegrationOutboxMessages
                .Where(m => m.Status == IntegrationOutboxStatus.Failed)
                .OrderBy(m => m.UpdatedUtc)
                .ThenBy(m => m.Id)
                .Take(take)
                .ToListAsync(cancellationToken);

            foreach (var message in failedMessages)
            {
                message.Status = IntegrationOutboxStatus.Pending;
                message.NextAttemptUtc = nowUtc;
                message.LastError = null;
                message.UpdatedUtc = nowUtc;
            }

            await _db.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                retried = failedMessages.Count,
                requestedTake = take
            });
        }

        [HttpPost("outbox/{id:int}/dead-letter")]
        public async Task<IActionResult> DeadLetterOutboxMessage(
            int id,
            [FromBody] DeadLetterOutboxRequest? request,
            CancellationToken cancellationToken = default)
        {
            var message = await _db.IntegrationOutboxMessages
                .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

            if (message is null)
            {
                return NotFound(new { message = "Outbox message not found." });
            }

            if (message.Status == IntegrationOutboxStatus.Processed)
            {
                return BadRequest(new { message = "Cannot dead-letter a processed outbox message." });
            }

            var actor = User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(actor))
            {
                actor = "unknown";
            }

            var reason = string.IsNullOrWhiteSpace(request?.Reason)
                ? "Manually moved to dead-letter queue."
                : request!.Reason.Trim();

            message.Status = IntegrationOutboxStatus.Failed;
            message.LastError = TrimToMax($"Dead-lettered by {actor}: {reason}", 2000);
            message.UpdatedUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            return Ok(new
            {
                id = message.Id,
                status = message.Status.ToString(),
                lastError = message.LastError
            });
        }

        [HttpGet("webhooks/paymongo/receipts")]
        public async Task<IActionResult> GetPayMongoReceipts(
            [FromQuery] string? status = null,
            [FromQuery] string? reference = null,
            [FromQuery] int take = 100,
            CancellationToken cancellationToken = default)
        {
            take = Math.Clamp(take, 1, 500);
            status = Normalize(status);
            reference = Normalize(reference);

            var query = _db.InboundWebhookReceipts
                .AsNoTracking()
                .Where(r => r.Provider == "PayMongo");

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(r => r.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(reference))
            {
                query = query.Where(r => r.ExternalReference == reference);
            }

            var receipts = await query
                .OrderByDescending(r => r.UpdatedUtc)
                .ThenByDescending(r => r.Id)
                .Take(take)
                .ToListAsync(cancellationToken);

            return Ok(receipts.Select(r => new
            {
                id = r.Id,
                provider = r.Provider,
                eventKey = r.EventKey,
                eventType = r.EventType,
                externalReference = r.ExternalReference,
                status = r.Status,
                attemptCount = r.AttemptCount,
                firstReceivedUtc = r.FirstReceivedUtc,
                lastAttemptUtc = r.LastAttemptUtc,
                processedUtc = r.ProcessedUtc,
                notes = r.Notes,
                createdUtc = r.CreatedUtc,
                updatedUtc = r.UpdatedUtc
            }));
        }

        [HttpPost("webhooks/paymongo/replay")]
        public async Task<IActionResult> ReplayPayMongoWebhook(
            [FromBody] ReplayPayMongoWebhookRequest request,
            CancellationToken cancellationToken = default)
        {
            var eventKey = Normalize(request.EventKey);
            var reference = Normalize(request.Reference);

            if (eventKey is null && reference is null)
            {
                return BadRequest(new { message = "Provide either eventKey or reference to replay." });
            }

            var nowUtc = DateTime.UtcNow;
            var receipt = await ResolveReceiptAsync(eventKey, reference, cancellationToken);
            var createdReceipt = false;
            if (receipt is null)
            {
                if (reference is null)
                {
                    return NotFound(new { message = "Webhook receipt not found for this event key." });
                }

                receipt = CreateSyntheticReceipt(reference, nowUtc);
                _db.InboundWebhookReceipts.Add(receipt);
                createdReceipt = true;
            }

            var statusBeforeReplay = receipt.Status;
            if (!request.Force &&
                string.Equals(receipt.Status, "Processing", StringComparison.OrdinalIgnoreCase) &&
                nowUtc - receipt.UpdatedUtc < TimeSpan.FromMinutes(2))
            {
                return Conflict(new
                {
                    message = "Webhook is currently processing. Retry with force=true if needed.",
                    receiptId = receipt.Id,
                    eventKey = receipt.EventKey
                });
            }

            if (!request.Force &&
                (string.Equals(receipt.Status, "Processed", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(receipt.Status, "Ignored", StringComparison.OrdinalIgnoreCase)))
            {
                return Conflict(new
                {
                    message = "Webhook is already completed. Retry with force=true to replay again.",
                    receiptId = receipt.Id,
                    status = receipt.Status
                });
            }

            var effectiveReference = reference ?? Normalize(receipt.ExternalReference);
            if (effectiveReference is null)
            {
                return BadRequest(new { message = "Reference is required to replay this webhook event." });
            }

            var payment = await _db.Payments
                .Include(p => p.Invoice)
                .FirstOrDefaultAsync(
                    p => p.ReferenceNumber == effectiveReference || p.GatewayPaymentId == effectiveReference,
                    cancellationToken);

            if (payment is null || payment.Invoice is null)
            {
                return NotFound(new { message = "Payment/invoice not found for replay reference.", reference = effectiveReference });
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            receipt.AttemptCount = Math.Max(1, receipt.AttemptCount + 1);
            receipt.Status = "Processing";
            receipt.LastAttemptUtc = nowUtc;
            receipt.UpdatedUtc = nowUtc;
            receipt.ExternalReference = effectiveReference;
            receipt.Notes = null;

            var actor = User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(actor))
            {
                actor = "unknown";
            }

            var replayKind = ResolveReplayKind(receipt.EventType, payment);
            var replayPayload = new
            {
                replay = new
                {
                    actor,
                    force = request.Force,
                    atUtc = nowUtc,
                    eventKey = receipt.EventKey,
                    reference = effectiveReference,
                    previousStatus = statusBeforeReplay,
                    createdReceipt
                },
                payment = new
                {
                    id = payment.Id,
                    invoiceId = payment.InvoiceId,
                    memberUserId = payment.Invoice.MemberUserId,
                    amount = payment.Amount,
                    paymentStatus = payment.Status.ToString(),
                    invoiceStatus = payment.Invoice.Status.ToString(),
                    gatewayProvider = payment.GatewayProvider,
                    gatewayPaymentId = payment.GatewayPaymentId,
                    checkoutSessionId = payment.ReferenceNumber,
                    subscriptionId = payment.Invoice.MemberSubscriptionId
                },
                receipt = new
                {
                    id = receipt.Id,
                    provider = receipt.Provider,
                    eventType = receipt.EventType,
                    eventKey = receipt.EventKey
                }
            };

            if (replayKind == ReplayKind.Paid)
            {
                await _outbox.EnqueueBackOfficeAsync(
                    "payment.succeeded",
                    "Replay: PayMongo payment success state re-emitted.",
                    replayPayload,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(payment.Invoice.MemberUserId))
                {
                    await _outbox.EnqueueUserAsync(
                        payment.Invoice.MemberUserId,
                        "payment.succeeded",
                        "Replay: your payment success state was synchronized.",
                        replayPayload,
                        cancellationToken);

                    if (payment.Invoice.MemberSubscriptionId.HasValue)
                    {
                        await _outbox.EnqueueUserAsync(
                            payment.Invoice.MemberUserId,
                            "membership.activated",
                            "Replay: your membership activation state was synchronized.",
                            replayPayload,
                            cancellationToken);
                    }
                }
            }
            else if (replayKind == ReplayKind.Failed)
            {
                await _outbox.EnqueueBackOfficeAsync(
                    "payment.failed",
                    "Replay: PayMongo payment failure state re-emitted.",
                    replayPayload,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(payment.Invoice.MemberUserId))
                {
                    await _outbox.EnqueueUserAsync(
                        payment.Invoice.MemberUserId,
                        "payment.failed",
                        "Replay: your payment failure state was synchronized.",
                        replayPayload,
                        cancellationToken);
                }
            }
            else
            {
                await _outbox.EnqueueBackOfficeAsync(
                    "paymongo.webhook.replay.unclassified",
                    "Replay requested for unclassified PayMongo event.",
                    replayPayload,
                    cancellationToken);
            }

            var processedUtc = DateTime.UtcNow;
            receipt.Status = "Processed";
            receipt.ProcessedUtc = processedUtc;
            receipt.UpdatedUtc = processedUtc;
            receipt.Notes = TrimToMax(
                $"Manually replayed by {actor} at {processedUtc:O}. Classification={replayKind}. Force={request.Force}.",
                2000);

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Ok(new
            {
                receiptId = receipt.Id,
                eventKey = receipt.EventKey,
                reference = effectiveReference,
                replayKind = replayKind.ToString(),
                outboxStatus = "Queued",
                createdReceipt
            });
        }

        private async Task<InboundWebhookReceipt?> ResolveReceiptAsync(
            string? eventKey,
            string? reference,
            CancellationToken cancellationToken)
        {
            if (eventKey is not null)
            {
                return await _db.InboundWebhookReceipts
                    .FirstOrDefaultAsync(
                        r => r.Provider == "PayMongo" && r.EventKey == eventKey,
                        cancellationToken);
            }

            if (reference is null)
            {
                return null;
            }

            return await _db.InboundWebhookReceipts
                .Where(r => r.Provider == "PayMongo" && r.ExternalReference == reference)
                .OrderByDescending(r => r.UpdatedUtc)
                .ThenByDescending(r => r.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        private static InboundWebhookReceipt CreateSyntheticReceipt(string reference, DateTime nowUtc)
        {
            return new InboundWebhookReceipt
            {
                Provider = "PayMongo",
                EventKey = $"manual-replay:{reference}:{Guid.NewGuid():N}",
                EventType = null,
                ExternalReference = reference,
                Status = "Pending",
                AttemptCount = 0,
                FirstReceivedUtc = nowUtc,
                LastAttemptUtc = nowUtc,
                CreatedUtc = nowUtc,
                UpdatedUtc = nowUtc
            };
        }

        private static ReplayKind ResolveReplayKind(string? eventType, Payment payment)
        {
            var normalizedType = Normalize(eventType);
            if (normalizedType is not null)
            {
                if (PaidEventTypes.Contains(normalizedType))
                {
                    return ReplayKind.Paid;
                }

                if (FailedEventTypes.Contains(normalizedType))
                {
                    return ReplayKind.Failed;
                }
            }

            if (payment.Status == PaymentStatus.Succeeded ||
                payment.Invoice?.Status == InvoiceStatus.Paid)
            {
                return ReplayKind.Paid;
            }

            if (payment.Status == PaymentStatus.Failed)
            {
                return ReplayKind.Failed;
            }

            return ReplayKind.Unclassified;
        }

        private static string? Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Trim();
        }

        private static string TrimToMax(string value, int maxLength)
        {
            return value.Length <= maxLength ? value : value[..maxLength];
        }

        private enum ReplayKind
        {
            Paid = 1,
            Failed = 2,
            Unclassified = 3
        }

        public sealed class RetryFailedOutboxRequest
        {
            public int Take { get; set; } = 50;
        }

        public sealed class DeadLetterOutboxRequest
        {
            public string? Reason { get; set; }
        }

        public sealed class ReplayPayMongoWebhookRequest
        {
            public string? EventKey { get; set; }
            public string? Reference { get; set; }
            public bool Force { get; set; }
        }
    }
}
