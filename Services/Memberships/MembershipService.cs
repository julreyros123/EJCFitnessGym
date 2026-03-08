using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Services.Integration;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Services.Memberships
{
    public class MembershipService : IMembershipService
    {
        private readonly ApplicationDbContext _db;
        private readonly IIntegrationOutbox? _integrationOutbox;
        private readonly IEmailSender? _emailSender;
        private readonly ILogger<MembershipService>? _logger;

        public MembershipService(
            ApplicationDbContext db,
            IIntegrationOutbox? integrationOutbox = null,
            IEmailSender? emailSender = null,
            ILogger<MembershipService>? logger = null)
        {
            _db = db;
            _integrationOutbox = integrationOutbox;
            _emailSender = emailSender;
            _logger = logger;
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
                .OrderBy(s => s.Status == SubscriptionStatus.Active
                    ? 0
                    : s.Status == SubscriptionStatus.Paused
                        ? 1
                        : 2)
                .ThenByDescending(s => s.EndDateUtc ?? DateTime.MinValue)
                .ThenByDescending(s => s.StartDateUtc)
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
            var generatedRenewalInvoices = 0;
            var threeDayRemindersQueued = 0;
            var voidedFailedCheckoutInvoices = 0;

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

            var failedCheckoutInvoicesToVoid = await _db.Invoices
                .Where(invoice =>
                    (invoice.Status == InvoiceStatus.Unpaid || invoice.Status == InvoiceStatus.Overdue) &&
                    invoice.MemberSubscriptionId == null &&
                    invoice.Notes != null &&
                    invoice.Notes.Contains("Subscription purchase:"))
                .Where(invoice =>
                    _db.Payments.Any(payment =>
                        payment.InvoiceId == invoice.Id &&
                        payment.Method == PaymentMethod.OnlineGateway &&
                        payment.GatewayProvider == "PayMongo"))
                .Where(invoice =>
                    !_db.Payments.Any(payment =>
                        payment.InvoiceId == invoice.Id &&
                        payment.Status == PaymentStatus.Pending))
                .Where(invoice =>
                    !_db.Payments.Any(payment =>
                        payment.InvoiceId == invoice.Id &&
                        payment.Status == PaymentStatus.Succeeded))
                .ToListAsync(cancellationToken);

            foreach (var invoice in failedCheckoutInvoicesToVoid)
            {
                invoice.Status = InvoiceStatus.Voided;
                voidedFailedCheckoutInvoices++;
            }

            var activeSubscriptions = await _db.MemberSubscriptions
                .Include(subscription => subscription.SubscriptionPlan)
                .Where(subscription =>
                    subscription.Status == SubscriptionStatus.Active &&
                    subscription.EndDateUtc.HasValue &&
                    subscription.EndDateUtc.Value > effectiveUtc &&
                    subscription.SubscriptionPlan != null &&
                    subscription.SubscriptionPlan.IsActive)
                .ToListAsync(cancellationToken);

            if (activeSubscriptions.Count > 0)
            {
                var activeSubscriptionIds = activeSubscriptions
                    .Select(subscription => subscription.Id)
                    .ToList();
                var activeMemberIds = activeSubscriptions
                    .Select(subscription => subscription.MemberUserId)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                var branchByMemberId = await MemberBranchAssignment.ResolveHomeBranchMapAsync(
                    _db,
                    activeMemberIds,
                    cancellationToken);

                var existingInvoiceKeys = await _db.Invoices
                    .AsNoTracking()
                    .Where(invoice =>
                        invoice.MemberSubscriptionId.HasValue &&
                        activeSubscriptionIds.Contains(invoice.MemberSubscriptionId.Value) &&
                        invoice.Status != InvoiceStatus.Voided)
                    .Select(invoice => new
                    {
                        SubscriptionId = invoice.MemberSubscriptionId!.Value,
                        invoice.DueDateUtc
                    })
                    .ToListAsync(cancellationToken);

                var existingSet = new HashSet<string>(StringComparer.Ordinal);
                foreach (var key in existingInvoiceKeys)
                {
                    existingSet.Add(BuildInvoiceCycleKey(key.SubscriptionId, key.DueDateUtc));
                }

                foreach (var subscription in activeSubscriptions)
                {
                    var dueDateUtc = subscription.EndDateUtc!.Value;
                    var cycleKey = BuildInvoiceCycleKey(subscription.Id, dueDateUtc);
                    if (existingSet.Contains(cycleKey))
                    {
                        continue;
                    }

                    var plan = subscription.SubscriptionPlan!;
                    var issueDateUtc = effectiveUtc < dueDateUtc ? effectiveUtc : dueDateUtc;
                    var noteLines = new List<string>
                    {
                        $"Renewal invoice for {plan.Name}.",
                        $"[sub:{subscription.Id}]",
                        $"[plan:{plan.Id}]",
                        $"[cycle:{dueDateUtc:yyyyMMdd}]"
                    };

                    _db.Invoices.Add(new Invoice
                    {
                        InvoiceNumber = GenerateInvoiceNumber(),
                        MemberUserId = subscription.MemberUserId,
                        BranchId = branchByMemberId.TryGetValue(subscription.MemberUserId, out var branchId) ? branchId : null,
                        MemberSubscriptionId = subscription.Id,
                        IssueDateUtc = issueDateUtc,
                        DueDateUtc = dueDateUtc,
                        Amount = plan.Price,
                        Status = InvoiceStatus.Unpaid,
                        Notes = string.Join(" ", noteLines)
                    });

                    existingSet.Add(cycleKey);
                    generatedRenewalInvoices++;
                }
            }

            var reminderTargetStartUtc = effectiveUtc.Date.AddDays(3);
            var reminderTargetEndUtc = reminderTargetStartUtc.AddDays(1);
            var invoicesForReminder = await _db.Invoices
                .Where(invoice =>
                    invoice.Status == InvoiceStatus.Unpaid &&
                    invoice.DueDateUtc >= reminderTargetStartUtc &&
                    invoice.DueDateUtc < reminderTargetEndUtc)
                .ToListAsync(cancellationToken);

            foreach (var invoice in invoicesForReminder)
            {
                var reminderMarker = BuildThreeDayReminderMarker(invoice.Id, invoice.DueDateUtc);
                if (HasReminderMarker(invoice.Notes, reminderMarker))
                {
                    continue;
                }

                if (_integrationOutbox is not null && !string.IsNullOrWhiteSpace(invoice.MemberUserId))
                {
                    var daysUntilDue = Math.Max(0, (invoice.DueDateUtc.Date - effectiveUtc.Date).Days);
                    await _integrationOutbox.EnqueueUserAsync(
                        invoice.MemberUserId,
                        "billing.invoice.reminder",
                        $"Billing reminder: Invoice {invoice.InvoiceNumber} is due on {invoice.DueDateUtc.ToLocalTime():yyyy-MM-dd HH:mm}.",
                        new
                        {
                            invoiceId = invoice.Id,
                            invoiceNumber = invoice.InvoiceNumber,
                            amount = invoice.Amount,
                            dueDateUtc = invoice.DueDateUtc,
                            daysUntilDue
                        },
                        cancellationToken);

                    await _integrationOutbox.EnqueueBackOfficeAsync(
                        "billing.invoice.reminder.queued",
                        "A 3-day billing reminder was queued for a member.",
                        new
                        {
                            invoiceId = invoice.Id,
                            invoiceNumber = invoice.InvoiceNumber,
                            memberUserId = invoice.MemberUserId,
                            amount = invoice.Amount,
                            dueDateUtc = invoice.DueDateUtc,
                            daysUntilDue
                        },
                        cancellationToken);

                    threeDayRemindersQueued++;
                }

                await TrySendDueReminderEmailAsync(invoice, effectiveUtc, cancellationToken);
                invoice.Notes = AppendReminderMarker(invoice.Notes, reminderMarker);
            }

            if (subscriptionsToExpire.Count > 0 ||
                invoicesToMarkOverdue.Count > 0 ||
                voidedFailedCheckoutInvoices > 0 ||
                generatedRenewalInvoices > 0 ||
                threeDayRemindersQueued > 0)
            {
                await _db.SaveChangesAsync(cancellationToken);
            }

            return new MembershipLifecycleMaintenanceResult
            {
                AsOfUtc = effectiveUtc,
                ExpiredSubscriptions = subscriptionsToExpire.Count,
                OverdueInvoices = invoicesToMarkOverdue.Count,
                GeneratedRenewalInvoices = generatedRenewalInvoices,
                ThreeDayRemindersQueued = threeDayRemindersQueued
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

        private static string GenerateInvoiceNumber()
        {
            return $"INV-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        }

        private static string BuildInvoiceCycleKey(int subscriptionId, DateTime dueDateUtc)
        {
            return $"{subscriptionId}:{dueDateUtc.Ticks}";
        }

        private static string BuildThreeDayReminderMarker(int invoiceId, DateTime dueDateUtc)
        {
            return $"[reminder-3d:{invoiceId}:{dueDateUtc:yyyyMMddHHmm}]";
        }

        private static bool HasReminderMarker(string? notes, string reminderMarker)
        {
            if (string.IsNullOrWhiteSpace(notes))
            {
                return false;
            }

            return notes.Contains(reminderMarker, StringComparison.Ordinal);
        }

        private static string AppendReminderMarker(string? notes, string reminderMarker)
        {
            if (string.IsNullOrWhiteSpace(notes))
            {
                return reminderMarker;
            }

            return $"{notes} {reminderMarker}".Trim();
        }

        private async Task TrySendDueReminderEmailAsync(Invoice invoice, DateTime effectiveUtc, CancellationToken cancellationToken)
        {
            if (_emailSender is null || string.IsNullOrWhiteSpace(invoice.MemberUserId))
            {
                return;
            }

            var recipientEmail = await _db.Users
                .AsNoTracking()
                .Where(user => user.Id == invoice.MemberUserId)
                .Select(user => user.Email)
                .FirstOrDefaultAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(recipientEmail))
            {
                return;
            }

            var daysUntilDue = Math.Max(0, (invoice.DueDateUtc.Date - effectiveUtc.Date).Days);
            var dueLocal = invoice.DueDateUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz");
            var amountLabel = invoice.Amount.ToString("N2");
            var subject = $"Payment due reminder - {invoice.InvoiceNumber}";
            var htmlMessage = $@"
<div style=""font-family: 'Inter', 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; max-width: 600px; margin: 0 auto; background-color: #f8fafc; padding: 40px 20px; border-radius: 12px;"">
    <div style=""background-color: #ffffff; padding: 40px; border-radius: 16px; box-shadow: 0 4px 15px rgba(0, 0, 0, 0.05);"">
        <div style=""text-align: center; margin-bottom: 30px;"">
            <h1 style=""color: #84cc16; margin: 0; font-size: 28px; font-weight: 800; letter-spacing: -0.5px;"">Fitness Gym</h1>
            <p style=""color: #64748b; margin-top: 8px; font-size: 15px; font-weight: 500;"">Payment Due Reminder</p>
        </div>
        
        <div style=""background-color: #fffbeb; border-radius: 12px; padding: 25px; text-align: center; border: 1px solid #fde68a; margin-bottom: 30px;"">
            <p style=""color: #b45309; font-size: 14px; margin: 0 0 5px 0; font-weight: 700; text-transform: uppercase; letter-spacing: 0.05em;"">Action Required</p>
            <h2 style=""color: #0f172a; font-size: 20px; margin: 0 0 10px 0; font-weight: 800;"">Invoice due in {daysUntilDue} day(s)</h2>
            <p style=""color: #92400e; font-size: 15px; margin: 0; line-height: 1.5;"">Please settle your invoice before the due date to keep your membership active and avoid interruptions.</p>
        </div>
        
        <table style=""width: 100%; border-collapse: collapse; margin-bottom: 30px;"">
            <tr>
                <td style=""padding: 14px 0; border-bottom: 1px solid #e2e8f0; color: #64748b; font-size: 15px;"">Invoice Number</td>
                <td style=""padding: 14px 0; border-bottom: 1px solid #e2e8f0; color: #0f172a; font-size: 15px; font-weight: 700; text-align: right;"">{invoice.InvoiceNumber}</td>
            </tr>
            <tr>
                <td style=""padding: 14px 0; border-bottom: 1px solid #e2e8f0; color: #64748b; font-size: 15px;"">Due Date</td>
                <td style=""padding: 14px 0; border-bottom: 1px solid #e2e8f0; color: #ef4444; font-size: 15px; font-weight: 700; text-align: right;"">{dueLocal}</td>
            </tr>
            <tr>
                <td style=""padding: 14px 0; color: #64748b; font-size: 15px;"">Amount Due</td>
                <td style=""padding: 14px 0; color: #0f172a; font-size: 15px; font-weight: 800; text-align: right;"">PHP {amountLabel}</td>
            </tr>
        </table>
        
        <div style=""text-align: center;"">
            <a href=""#"" style=""display: inline-block; background-color: #84cc16; color: #ffffff; font-weight: 700; font-size: 15px; text-decoration: none; padding: 12px 24px; border-radius: 8px; box-shadow: 0 2px 4px rgba(132, 204, 22, 0.3);"">Login to Member Portal</a>
        </div>
    </div>
    
    <div style=""text-align: center; margin-top: 30px; color: #94a3b8; font-size: 13px; line-height: 1.5;"">
        <p style=""margin: 0;"">&copy; {DateTime.UtcNow.Year} Fitness Gym. All rights reserved.</p>
        <p style=""margin: 5px 0 0 0;"">This is an automated reminder. Please do not reply to this email.</p>
    </div>
</div>";

            try
            {
                await _emailSender.SendEmailAsync(recipientEmail, subject, htmlMessage);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(
                    ex,
                    "Could not send due reminder email for invoice {InvoiceId} to member {MemberUserId}.",
                    invoice.Id,
                    invoice.MemberUserId);
            }
        }
    }
}
