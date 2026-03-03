using System.Globalization;
using System.Text.RegularExpressions;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Services.Memberships;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Services.Payments
{
    public class PayMongoMembershipReconciliationService : IPayMongoMembershipReconciliationService
    {
        private static readonly Regex PlanTokenRegex = new(@"\[plan:(\d+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly ApplicationDbContext _db;
        private readonly IMembershipService _membershipService;
        private readonly PayMongoClient _payMongoClient;
        private readonly PayMongoOptions _payMongoOptions;
        private readonly ILogger<PayMongoMembershipReconciliationService> _logger;

        public PayMongoMembershipReconciliationService(
            ApplicationDbContext db,
            IMembershipService membershipService,
            PayMongoClient payMongoClient,
            Microsoft.Extensions.Options.IOptions<PayMongoOptions> payMongoOptions,
            ILogger<PayMongoMembershipReconciliationService> logger)
        {
            _db = db;
            _membershipService = membershipService;
            _payMongoClient = payMongoClient;
            _payMongoOptions = payMongoOptions.Value;
            _logger = logger;
        }

        public async Task<int> ReconcilePendingMemberPaymentsAsync(
            string memberUserId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(memberUserId))
            {
                return 0;
            }

            if (string.IsNullOrWhiteSpace(_payMongoOptions.SecretKey))
            {
                return 0;
            }

            var normalizedMemberUserId = memberUserId.Trim();
            var unsettledPayments = await _db.Payments
                .Include(payment => payment.Invoice)
                .Where(payment =>
                    (payment.Status == PaymentStatus.Pending || payment.Status == PaymentStatus.Failed) &&
                    payment.Method == PaymentMethod.OnlineGateway &&
                    payment.GatewayProvider == "PayMongo" &&
                    payment.ReferenceNumber != null &&
                    payment.Invoice != null &&
                    payment.Invoice.MemberUserId == normalizedMemberUserId)
                .OrderByDescending(payment => payment.PaidAtUtc)
                .ThenByDescending(payment => payment.Id)
                .Take(16)
                .ToListAsync(cancellationToken);

            var changes = 0;
            foreach (var payment in unsettledPayments)
            {
                if (payment.Invoice is null || string.IsNullOrWhiteSpace(payment.ReferenceNumber))
                {
                    continue;
                }

                PayMongoCheckoutSessionLookupResult checkout;
                try
                {
                    checkout = await _payMongoClient.GetCheckoutSessionAsync(payment.ReferenceNumber, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Could not reconcile pending PayMongo payment {PaymentId} for member {MemberUserId} (checkout session {CheckoutSessionId}).",
                        payment.Id,
                        normalizedMemberUserId,
                        payment.ReferenceNumber);
                    continue;
                }

                if (checkout.IsPaid)
                {
                    try
                    {
                        if (await ApplyPaidReconciliationAsync(payment, checkout, cancellationToken))
                        {
                            changes++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "PayMongo paid reconciliation failed for payment {PaymentId}, member {MemberUserId}, checkout {CheckoutSessionId}.",
                            payment.Id,
                            normalizedMemberUserId,
                            payment.ReferenceNumber);
                    }

                    continue;
                }

                if (checkout.IsFailedOrExpired)
                {
                    try
                    {
                        if (await ApplyFailedReconciliationAsync(payment, checkout, cancellationToken))
                        {
                            changes++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "PayMongo failed/expired reconciliation failed for payment {PaymentId}, member {MemberUserId}, checkout {CheckoutSessionId}.",
                            payment.Id,
                            normalizedMemberUserId,
                            payment.ReferenceNumber);
                    }
                }
            }

            if (changes > 0)
            {
                try
                {
                    await _membershipService.RunLifecycleMaintenanceAsync(cancellationToken: cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Post-reconciliation lifecycle maintenance failed for member {MemberUserId}.",
                        normalizedMemberUserId);
                }
            }

            return changes;
        }

        private async Task<bool> ApplyPaidReconciliationAsync(
            Payment payment,
            PayMongoCheckoutSessionLookupResult checkout,
            CancellationToken cancellationToken)
        {
            if (payment.Invoice is null)
            {
                return false;
            }

            var nowUtc = DateTime.UtcNow;
            var paidAtUtc = checkout.PaidAtUtc ?? nowUtc;
            var hasUpdates = false;

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            if (checkout.PaidAmount.HasValue && checkout.PaidAmount.Value > 0m)
            {
                payment.Amount = checkout.PaidAmount.Value;
                hasUpdates = true;
            }

            if (payment.Method != PaymentMethod.OnlineGateway)
            {
                payment.Method = PaymentMethod.OnlineGateway;
                hasUpdates = true;
            }

            if (payment.Status != PaymentStatus.Succeeded)
            {
                payment.Status = PaymentStatus.Succeeded;
                hasUpdates = true;
            }

            if (!string.Equals(payment.GatewayProvider, "PayMongo", StringComparison.OrdinalIgnoreCase))
            {
                payment.GatewayProvider = "PayMongo";
                hasUpdates = true;
            }

            if (!string.IsNullOrWhiteSpace(checkout.PaymentId) &&
                !string.Equals(payment.GatewayPaymentId, checkout.PaymentId, StringComparison.OrdinalIgnoreCase))
            {
                payment.GatewayPaymentId = checkout.PaymentId;
                hasUpdates = true;
            }

            if (string.IsNullOrWhiteSpace(payment.ReferenceNumber))
            {
                payment.ReferenceNumber = checkout.CheckoutSessionId;
                hasUpdates = true;
            }

            if (payment.PaidAtUtc != paidAtUtc)
            {
                payment.PaidAtUtc = paidAtUtc;
                hasUpdates = true;
            }

            var historicalSucceededAmounts = await _db.Payments
                .AsNoTracking()
                .Where(existing =>
                    existing.InvoiceId == payment.InvoiceId &&
                    existing.Id != payment.Id &&
                    existing.Status == PaymentStatus.Succeeded)
                .Select(existing => existing.Amount)
                .ToListAsync(cancellationToken);
            var historicalSucceededTotal = historicalSucceededAmounts.Sum();
            var successfulPaidTotal = historicalSucceededTotal + payment.Amount;
            var targetInvoiceStatus = InvoiceStatusPolicy.ResolveAfterSuccessfulPayment(
                payment.Invoice,
                successfulPaidTotal,
                paidAtUtc);

            if (payment.Invoice.Status != targetInvoiceStatus)
            {
                payment.Invoice.Status = targetInvoiceStatus;
                hasUpdates = true;
            }

            var resolvedMemberUserId = !string.IsNullOrWhiteSpace(payment.Invoice.MemberUserId)
                ? payment.Invoice.MemberUserId
                : TryGetMetadataValue(checkout.Metadata, "member_user_id");

            var resolvedPlanId = TryGetMetadataInt(checkout.Metadata, "plan_id");
            if (!resolvedPlanId.HasValue && payment.Invoice.MemberSubscriptionId.HasValue)
            {
                resolvedPlanId = await _db.MemberSubscriptions
                    .AsNoTracking()
                    .Where(subscription => subscription.Id == payment.Invoice.MemberSubscriptionId.Value)
                    .Select(subscription => (int?)subscription.SubscriptionPlanId)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            resolvedPlanId ??= ExtractPlanIdFromInvoiceNotes(payment.Invoice.Notes);

            if (targetInvoiceStatus == InvoiceStatus.Paid &&
                !string.IsNullOrWhiteSpace(resolvedMemberUserId) &&
                resolvedPlanId.HasValue)
            {
                var subscription = await _membershipService.ActivateSubscriptionAsync(
                    resolvedMemberUserId,
                    resolvedPlanId.Value,
                    externalSubscriptionId: checkout.CheckoutSessionId,
                    externalCustomerId: TryGetMetadataValue(checkout.Metadata, "customer_id"),
                    cancellationToken: cancellationToken);

                payment.Invoice.MemberSubscription = subscription;
                if (subscription.Id > 0 && payment.Invoice.MemberSubscriptionId != subscription.Id)
                {
                    payment.Invoice.MemberSubscriptionId = subscription.Id;
                }

                hasUpdates = true;
            }
            else
            {
                if (targetInvoiceStatus != InvoiceStatus.Paid)
                {
                    _logger.LogWarning(
                        "PayMongo paid reconciliation for payment {PaymentId} left invoice {InvoiceId} as {InvoiceStatus} due to insufficient paid total.",
                        payment.Id,
                        payment.InvoiceId,
                        targetInvoiceStatus);
                }
                else
                {
                    _logger.LogWarning(
                        "PayMongo paid reconciliation for payment {PaymentId} could not resolve member or plan (member: {MemberUserId}, plan: {PlanId}).",
                        payment.Id,
                        resolvedMemberUserId,
                        resolvedPlanId);
                }
            }

            if (!hasUpdates)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Reconciled PayMongo paid checkout {CheckoutSessionId} for member {MemberUserId}.",
                checkout.CheckoutSessionId,
                resolvedMemberUserId ?? payment.Invoice.MemberUserId);

            return true;
        }

        private async Task<bool> ApplyFailedReconciliationAsync(
            Payment payment,
            PayMongoCheckoutSessionLookupResult checkout,
            CancellationToken cancellationToken)
        {
            if (payment.Invoice is null)
            {
                return false;
            }

            if (payment.Status == PaymentStatus.Succeeded)
            {
                return false;
            }

            var nowUtc = DateTime.UtcNow;
            var changed = false;

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            if (payment.Status != PaymentStatus.Failed)
            {
                payment.Status = PaymentStatus.Failed;
                changed = true;
            }

            if (payment.Method != PaymentMethod.OnlineGateway)
            {
                payment.Method = PaymentMethod.OnlineGateway;
                changed = true;
            }

            if (!string.Equals(payment.GatewayProvider, "PayMongo", StringComparison.OrdinalIgnoreCase))
            {
                payment.GatewayProvider = "PayMongo";
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(checkout.PaymentId) &&
                !string.Equals(payment.GatewayPaymentId, checkout.PaymentId, StringComparison.OrdinalIgnoreCase))
            {
                payment.GatewayPaymentId = checkout.PaymentId;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(payment.ReferenceNumber))
            {
                payment.ReferenceNumber = checkout.CheckoutSessionId;
                changed = true;
            }

            if (payment.PaidAtUtc != nowUtc)
            {
                payment.PaidAtUtc = nowUtc;
                changed = true;
            }

            var succeededAmounts = await _db.Payments
                .AsNoTracking()
                .Where(existing =>
                    existing.InvoiceId == payment.InvoiceId &&
                    existing.Status == PaymentStatus.Succeeded)
                .Select(existing => existing.Amount)
                .ToListAsync(cancellationToken);
            var successfulPaidTotal = succeededAmounts.Sum();

            var targetInvoiceStatus = InvoiceStatusPolicy.ResolveAfterFailedCheckoutAttempt(
                payment.Invoice,
                successfulPaidTotal,
                nowUtc);

            if (payment.Invoice.Status != targetInvoiceStatus)
            {
                payment.Invoice.Status = targetInvoiceStatus;
                changed = true;
            }

            if (!changed)
            {
                await transaction.RollbackAsync(cancellationToken);
                return false;
            }

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return true;
        }

        private static string? TryGetMetadataValue(IReadOnlyDictionary<string, string> metadata, string key)
        {
            return metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : null;
        }

        private static int? TryGetMetadataInt(IReadOnlyDictionary<string, string> metadata, string key)
        {
            var value = TryGetMetadataValue(metadata, key);
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
    }
}
