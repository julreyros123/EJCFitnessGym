using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Services.Memberships
{
    public class MembershipService : IMembershipService
    {
        private readonly ApplicationDbContext _db;

        public MembershipService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<MemberSubscription?> GetLatestSubscriptionAsync(string memberUserId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(memberUserId))
            {
                return null;
            }

            return await _db.MemberSubscriptions
                .AsNoTracking()
                .Where(s => s.MemberUserId == memberUserId)
                .Include(s => s.SubscriptionPlan)
                .OrderByDescending(s => s.StartDateUtc)
                .ThenByDescending(s => s.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<MemberSubscription>> GetSubscriptionHistoryAsync(
            string memberUserId,
            int take = 12,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(memberUserId) || take <= 0)
            {
                return Array.Empty<MemberSubscription>();
            }

            var cappedTake = Math.Min(take, 100);

            return await _db.MemberSubscriptions
                .AsNoTracking()
                .Where(s => s.MemberUserId == memberUserId)
                .Include(s => s.SubscriptionPlan)
                .OrderByDescending(s => s.StartDateUtc)
                .ThenByDescending(s => s.Id)
                .Take(cappedTake)
                .ToListAsync(cancellationToken);
        }

        public async Task<MemberSubscription> ActivateSubscriptionAsync(
            string memberUserId,
            int planId,
            DateTime? startDateUtc = null,
            string? externalSubscriptionId = null,
            string? externalCustomerId = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(memberUserId))
            {
                throw new ArgumentException("Member user id is required.", nameof(memberUserId));
            }

            if (planId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(planId), "Plan id must be greater than zero.");
            }

            var selectedPlan = await _db.SubscriptionPlans
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);

            if (selectedPlan is null)
            {
                throw new InvalidOperationException($"Subscription plan '{planId}' was not found.");
            }

            var nowUtc = DateTime.UtcNow;
            var explicitStartDateProvided = startDateUtc.HasValue;
            var normalizedStartDateUtc = ToUtc(startDateUtc ?? nowUtc);
            var normalizedExternalSubscriptionId = string.IsNullOrWhiteSpace(externalSubscriptionId)
                ? null
                : externalSubscriptionId.Trim();
            var normalizedExternalCustomerId = string.IsNullOrWhiteSpace(externalCustomerId)
                ? null
                : externalCustomerId.Trim();

            MemberSubscription? targetSubscription = null;
            var matchedByExternalReference = false;

            if (!string.IsNullOrWhiteSpace(normalizedExternalSubscriptionId))
            {
                targetSubscription = await _db.MemberSubscriptions
                    .FirstOrDefaultAsync(
                        s => s.ExternalSubscriptionId == normalizedExternalSubscriptionId,
                        cancellationToken);
                matchedByExternalReference = targetSubscription is not null;
            }

            if (targetSubscription is null && !explicitStartDateProvided)
            {
                targetSubscription = await _db.MemberSubscriptions
                    .Where(s =>
                        s.MemberUserId == memberUserId &&
                        s.SubscriptionPlanId == selectedPlan.Id &&
                        (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Paused))
                    .OrderByDescending(s => s.EndDateUtc ?? DateTime.MinValue)
                    .ThenByDescending(s => s.StartDateUtc)
                    .ThenByDescending(s => s.Id)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (targetSubscription is null)
            {
                targetSubscription = new MemberSubscription
                {
                    MemberUserId = memberUserId,
                    SubscriptionPlanId = selectedPlan.Id,
                    StartDateUtc = normalizedStartDateUtc,
                    EndDateUtc = CalculateEndDate(normalizedStartDateUtc, selectedPlan.BillingCycle),
                    Status = SubscriptionStatus.Active,
                    ExternalCustomerId = normalizedExternalCustomerId,
                    ExternalSubscriptionId = normalizedExternalSubscriptionId
                };

                _db.MemberSubscriptions.Add(targetSubscription);
            }
            else
            {
                targetSubscription.MemberUserId = memberUserId;
                targetSubscription.SubscriptionPlanId = selectedPlan.Id;
                targetSubscription.Status = SubscriptionStatus.Active;
                targetSubscription.ExternalCustomerId = string.IsNullOrWhiteSpace(normalizedExternalCustomerId)
                    ? targetSubscription.ExternalCustomerId
                    : normalizedExternalCustomerId;
                targetSubscription.ExternalSubscriptionId = string.IsNullOrWhiteSpace(normalizedExternalSubscriptionId)
                    ? targetSubscription.ExternalSubscriptionId
                    : normalizedExternalSubscriptionId;

                if (targetSubscription.StartDateUtc == default || targetSubscription.StartDateUtc > normalizedStartDateUtc)
                {
                    targetSubscription.StartDateUtc = normalizedStartDateUtc;
                }

                if (!matchedByExternalReference)
                {
                    var renewalAnchorUtc = normalizedStartDateUtc;
                    if (!explicitStartDateProvided &&
                        targetSubscription.EndDateUtc.HasValue &&
                        targetSubscription.EndDateUtc.Value > renewalAnchorUtc)
                    {
                        renewalAnchorUtc = targetSubscription.EndDateUtc.Value;
                    }

                    targetSubscription.EndDateUtc = CalculateEndDate(renewalAnchorUtc, selectedPlan.BillingCycle);
                }
            }

            var subscriptionsToDeactivate = await _db.MemberSubscriptions
                .Where(s =>
                    s.MemberUserId == memberUserId &&
                    (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Paused) &&
                    (targetSubscription.Id == 0 || s.Id != targetSubscription.Id))
                .ToListAsync(cancellationToken);

            foreach (var existing in subscriptionsToDeactivate)
            {
                existing.Status = SubscriptionStatus.Cancelled;
                if (!existing.EndDateUtc.HasValue || existing.EndDateUtc.Value > nowUtc)
                {
                    existing.EndDateUtc = nowUtc;
                }
            }

            return targetSubscription;
        }

        public async Task<MemberSubscription?> ResumeSubscriptionAsync(
            string memberUserId,
            DateTime? resumeAtUtc = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(memberUserId))
            {
                return null;
            }

            var subscription = await _db.MemberSubscriptions
                .Include(s => s.SubscriptionPlan)
                .Where(s => s.MemberUserId == memberUserId)
                .OrderByDescending(s => s.StartDateUtc)
                .ThenByDescending(s => s.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (subscription is null)
            {
                return null;
            }

            if (subscription.Status == SubscriptionStatus.Cancelled || subscription.Status == SubscriptionStatus.Expired)
            {
                return null;
            }

            if (subscription.Status == SubscriptionStatus.Active)
            {
                return subscription;
            }

            var effectiveUtc = ToUtc(resumeAtUtc ?? DateTime.UtcNow);
            subscription.Status = SubscriptionStatus.Active;

            if (subscription.EndDateUtc.HasValue &&
                subscription.EndDateUtc.Value < effectiveUtc &&
                subscription.SubscriptionPlan is not null)
            {
                subscription.EndDateUtc = CalculateEndDate(effectiveUtc, subscription.SubscriptionPlan.BillingCycle);
                if (subscription.StartDateUtc == default || subscription.StartDateUtc > effectiveUtc)
                {
                    subscription.StartDateUtc = effectiveUtc;
                }
            }

            return subscription;
        }

        public async Task<MembershipLifecycleMaintenanceResult> RunLifecycleMaintenanceAsync(
            DateTime? asOfUtc = null,
            CancellationToken cancellationToken = default)
        {
            var effectiveUtc = ToUtc(asOfUtc ?? DateTime.UtcNow);

            var subscriptionsToExpire = await _db.MemberSubscriptions
                .Where(s =>
                    (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Paused) &&
                    s.EndDateUtc.HasValue &&
                    s.EndDateUtc.Value < effectiveUtc)
                .ToListAsync(cancellationToken);

            foreach (var subscription in subscriptionsToExpire)
            {
                subscription.Status = SubscriptionStatus.Expired;
            }

            var invoicesToMarkOverdue = await _db.Invoices
                .Where(i =>
                    i.Status == InvoiceStatus.Unpaid &&
                    i.DueDateUtc < effectiveUtc)
                .ToListAsync(cancellationToken);

            foreach (var invoice in invoicesToMarkOverdue)
            {
                invoice.Status = InvoiceStatus.Overdue;
            }

            if (subscriptionsToExpire.Count > 0 || invoicesToMarkOverdue.Count > 0)
            {
                await _db.SaveChangesAsync(cancellationToken);
            }

            return new MembershipLifecycleMaintenanceResult
            {
                AsOfUtc = effectiveUtc,
                ExpiredSubscriptions = subscriptionsToExpire.Count,
                OverdueInvoices = invoicesToMarkOverdue.Count
            };
        }

        private static DateTime CalculateEndDate(DateTime startDateUtc, BillingCycle billingCycle)
        {
            var normalizedStartDate = ToUtc(startDateUtc);
            return billingCycle switch
            {
                BillingCycle.Weekly => normalizedStartDate.AddDays(7),
                BillingCycle.Yearly => normalizedStartDate.AddYears(1),
                _ => normalizedStartDate.AddMonths(1)
            };
        }

        private static DateTime ToUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc)
            {
                return value;
            }

            return value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                : value.ToUniversalTime();
        }
    }
}
