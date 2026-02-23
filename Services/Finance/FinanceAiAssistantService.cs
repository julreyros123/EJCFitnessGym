using System.Text.Json;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Models.Finance;
using EJCFitnessGym.Security;
using EJCFitnessGym.Services.AI;
using EJCFitnessGym.Services.Realtime;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EJCFitnessGym.Services.Finance
{
    public sealed class FinanceAiAssistantService : IFinanceAiAssistantService
    {
        private const string HighChurnAlertType = "FinanceMemberChurnHigh";
        private readonly ApplicationDbContext _db;
        private readonly IMemberChurnRiskService _memberChurnRiskService;
        private readonly IErpEventPublisher _erpEventPublisher;
        private readonly IEmailSender _emailSender;
        private readonly FinanceAlertOptions _financeAlertOptions;
        private readonly ILogger<FinanceAiAssistantService> _logger;

        public FinanceAiAssistantService(
            ApplicationDbContext db,
            IMemberChurnRiskService memberChurnRiskService,
            IErpEventPublisher erpEventPublisher,
            IEmailSender emailSender,
            IOptions<FinanceAlertOptions> financeAlertOptions,
            ILogger<FinanceAiAssistantService> logger)
        {
            _db = db;
            _memberChurnRiskService = memberChurnRiskService;
            _erpEventPublisher = erpEventPublisher;
            _emailSender = emailSender;
            _financeAlertOptions = financeAlertOptions.Value;
            _logger = logger;
        }

        public async Task<FinanceAiOverviewDto> GetBranchAiOverviewAsync(
            string? branchId,
            DateTime? fromUtc = null,
            DateTime? toUtc = null,
            int priorityTake = 12,
            CancellationToken cancellationToken = default)
        {
            var context = await BuildBranchContextAsync(
                branchId,
                fromUtc,
                toUtc,
                priorityTake,
                cancellationToken);

            return context.Overview;
        }

        public async Task<FinanceHighRiskAlertDispatchResultDto> DispatchNewHighRiskAlertsAsync(
            string trigger,
            DateTime? fromUtc = null,
            DateTime? toUtc = null,
            CancellationToken cancellationToken = default)
        {
            var evaluatedAtUtc = DateTime.UtcNow;
            if (!_financeAlertOptions.Enabled)
            {
                return new FinanceHighRiskAlertDispatchResultDto
                {
                    BranchesEvaluated = 0,
                    HighRiskMembersEvaluated = 0,
                    AlertsSent = 0,
                    EvaluatedAtUtc = evaluatedAtUtc
                };
            }

            var branchIds = await ResolveMemberBranchIdsAsync(cancellationToken);
            var alertsSent = 0;
            var highRiskMembersEvaluated = 0;

            foreach (var branchId in branchIds)
            {
                var context = await BuildBranchContextAsync(
                    branchId,
                    fromUtc,
                    toUtc,
                    priorityTake: int.MaxValue,
                    cancellationToken);

                var highRiskMembers = context.Overview.PriorityMembers
                    .Where(member => string.Equals(member.RiskLevel, "High", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                highRiskMembersEvaluated += highRiskMembers.Count;
                foreach (var member in highRiskMembers)
                {
                    var wasSent = await SendHighRiskMemberAlertIfDueAsync(
                        member,
                        branchId,
                        trigger,
                        cancellationToken);
                    if (wasSent)
                    {
                        alertsSent += 1;
                    }
                }
            }

            return new FinanceHighRiskAlertDispatchResultDto
            {
                BranchesEvaluated = branchIds.Count,
                HighRiskMembersEvaluated = highRiskMembersEvaluated,
                AlertsSent = alertsSent,
                EvaluatedAtUtc = evaluatedAtUtc
            };
        }

        private async Task<BranchAiContext> BuildBranchContextAsync(
            string? branchId,
            DateTime? fromUtc,
            DateTime? toUtc,
            int priorityTake,
            CancellationToken cancellationToken)
        {
            var utcNow = DateTime.UtcNow;
            var normalizedToUtc = NormalizeToUtc(toUtc) ?? utcNow;
            var normalizedFromUtc = NormalizeToUtc(fromUtc) ?? normalizedToUtc.AddDays(-30);
            if (normalizedFromUtc > normalizedToUtc)
            {
                (normalizedFromUtc, normalizedToUtc) = (normalizedToUtc, normalizedFromUtc);
            }

            if (string.IsNullOrWhiteSpace(branchId))
            {
                return new BranchAiContext(
                    new FinanceAiOverviewDto
                    {
                        BranchId = null,
                        GeneratedAtUtc = utcNow,
                        FromUtc = normalizedFromUtc,
                        ToUtc = normalizedToUtc
                    });
            }

            var scopedMemberIds = await ResolveScopedMemberIdsAsync(branchId, cancellationToken);
            if (scopedMemberIds.Count == 0)
            {
                return new BranchAiContext(
                    new FinanceAiOverviewDto
                    {
                        BranchId = branchId,
                        GeneratedAtUtc = utcNow,
                        FromUtc = normalizedFromUtc,
                        ToUtc = normalizedToUtc
                    });
            }

            var memberEmailsById = await _db.Users
                .AsNoTracking()
                .Where(user => scopedMemberIds.Contains(user.Id))
                .Select(user => new
                {
                    user.Id,
                    Email = user.Email ?? user.UserName ?? user.Id
                })
                .ToDictionaryAsync(item => item.Id, item => item.Email, StringComparer.Ordinal, cancellationToken);

            var paymentFactsInRange = await (
                from invoice in _db.Invoices.AsNoTracking()
                join payment in _db.Payments.AsNoTracking()
                    on invoice.Id equals payment.InvoiceId
                where scopedMemberIds.Contains(invoice.MemberUserId) &&
                      payment.Status == PaymentStatus.Succeeded &&
                      payment.PaidAtUtc >= normalizedFromUtc &&
                      payment.PaidAtUtc <= normalizedToUtc
                select new
                {
                    invoice.MemberUserId,
                    payment.Amount
                })
                .ToListAsync(cancellationToken);

            var paymentStatsByMember = paymentFactsInRange
                .GroupBy(item => item.MemberUserId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => (
                        TotalSpending: (float)group.Sum(item => item.Amount),
                        BillingActivityCount: (float)group.Count()),
                    StringComparer.Ordinal);

            var lastSuccessfulPaymentByMember = await (
                from invoice in _db.Invoices.AsNoTracking()
                join payment in _db.Payments.AsNoTracking()
                    on invoice.Id equals payment.InvoiceId
                where scopedMemberIds.Contains(invoice.MemberUserId) &&
                      payment.Status == PaymentStatus.Succeeded &&
                      payment.PaidAtUtc <= normalizedToUtc
                group payment by invoice.MemberUserId
                into grouped
                select new
                {
                    MemberUserId = grouped.Key,
                    LastPaymentUtc = grouped.Max(item => item.PaidAtUtc)
                })
                .ToDictionaryAsync(
                    item => item.MemberUserId,
                    item => (DateTime?)item.LastPaymentUtc,
                    StringComparer.Ordinal,
                    cancellationToken);

            var overdueInvoices = await _db.Invoices
                .AsNoTracking()
                .Where(invoice =>
                    scopedMemberIds.Contains(invoice.MemberUserId) &&
                    invoice.Status == InvoiceStatus.Overdue)
                .Select(invoice => new
                {
                    invoice.MemberUserId,
                    invoice.Amount
                })
                .ToListAsync(cancellationToken);

            var overdueStatsByMember = overdueInvoices
                .GroupBy(invoice => invoice.MemberUserId, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => (
                        Count: group.Count(),
                        Amount: group.Sum(item => item.Amount)),
                    StringComparer.Ordinal);

            var openInvoices = await _db.Invoices
                .AsNoTracking()
                .Where(invoice =>
                    scopedMemberIds.Contains(invoice.MemberUserId) &&
                    (invoice.Status == InvoiceStatus.Unpaid || invoice.Status == InvoiceStatus.Overdue))
                .Select(invoice => new
                {
                    invoice.MemberUserId,
                    invoice.Amount
                })
                .ToListAsync(cancellationToken);

            var openInvoiceCount = openInvoices.Count;
            var openInvoiceExposureAmount = openInvoices.Sum(invoice => invoice.Amount);

            var latestSubscriptions = await _db.MemberSubscriptions
                .AsNoTracking()
                .Where(subscription => scopedMemberIds.Contains(subscription.MemberUserId))
                .GroupBy(subscription => subscription.MemberUserId)
                .Select(group => group
                    .OrderByDescending(subscription => subscription.StartDateUtc)
                    .ThenByDescending(subscription => subscription.Id)
                    .Select(subscription => new
                    {
                        subscription.MemberUserId,
                        subscription.Status,
                        subscription.EndDateUtc
                    })
                    .First())
                .ToListAsync(cancellationToken);

            var latestSubscriptionByMember = latestSubscriptions.ToDictionary(
                subscription => subscription.MemberUserId,
                subscription => subscription,
                StringComparer.Ordinal);

            var memberTenureByUserId = await _db.MemberSubscriptions
                .AsNoTracking()
                .Where(subscription => scopedMemberIds.Contains(subscription.MemberUserId))
                .GroupBy(subscription => subscription.MemberUserId)
                .Select(group => new
                {
                    MemberUserId = group.Key,
                    FirstStartUtc = group.Min(subscription => subscription.StartDateUtc)
                })
                .ToDictionaryAsync(
                    item => item.MemberUserId,
                    item => (float)Math.Max(0d, (normalizedToUtc.Date - item.FirstStartUtc.Date).TotalDays / 30.4375d),
                    StringComparer.Ordinal,
                    cancellationToken);

            var anchorDateUtc = normalizedToUtc.Date;
            var churnInputs = scopedMemberIds
                .Select(memberUserId =>
                {
                    paymentStatsByMember.TryGetValue(memberUserId, out var paymentStats);
                    lastSuccessfulPaymentByMember.TryGetValue(memberUserId, out var lastPaymentUtc);
                    overdueStatsByMember.TryGetValue(memberUserId, out var overdueStats);
                    latestSubscriptionByMember.TryGetValue(memberUserId, out var subscription);
                    memberTenureByUserId.TryGetValue(memberUserId, out var membershipMonths);

                    var hasActiveMembership = subscription is not null &&
                        subscription.Status == SubscriptionStatus.Active &&
                        (!subscription.EndDateUtc.HasValue || subscription.EndDateUtc.Value.Date >= anchorDateUtc);

                    return new MemberChurnRiskInput
                    {
                        MemberUserId = memberUserId,
                        DisplayName = memberEmailsById.TryGetValue(memberUserId, out var email) ? email : memberUserId,
                        TotalSpending = paymentStats.TotalSpending,
                        BillingActivityCount = paymentStats.BillingActivityCount,
                        MembershipMonths = membershipMonths,
                        DaysSinceLastSuccessfulPayment = lastPaymentUtc.HasValue
                            ? (float?)(normalizedToUtc - lastPaymentUtc.Value).TotalDays
                            : null,
                        DaysUntilMembershipEnd = subscription?.EndDateUtc.HasValue == true
                            ? (float?)(subscription.EndDateUtc.Value.Date - anchorDateUtc).TotalDays
                            : null,
                        OverdueInvoiceCount = overdueStats.Count,
                        HasActiveMembership = hasActiveMembership
                    };
                })
                .ToList();

            var churnRisk = _memberChurnRiskService.PredictRisk(churnInputs);
            var highRiskCount = churnRisk.LevelSummary
                .FirstOrDefault(item => string.Equals(item.RiskLevel, "High", StringComparison.OrdinalIgnoreCase))
                ?.MemberCount ?? 0;
            var mediumRiskCount = churnRisk.LevelSummary
                .FirstOrDefault(item => string.Equals(item.RiskLevel, "Medium", StringComparison.OrdinalIgnoreCase))
                ?.MemberCount ?? 0;

            var renewalsDueCount = latestSubscriptions.Count(subscription =>
                subscription.Status == SubscriptionStatus.Active &&
                subscription.EndDateUtc.HasValue &&
                subscription.EndDateUtc.Value.Date >= anchorDateUtc &&
                subscription.EndDateUtc.Value.Date <= anchorDateUtc.AddDays(30));

            var prioritizedAtRiskMembers = churnRisk.ResultsByMemberId.Values
                .Where(result =>
                    string.Equals(result.RiskLevel, "High", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(result.RiskLevel, "Medium", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(result => result.RiskScore)
                .ThenBy(result => result.MemberUserId, StringComparer.Ordinal)
                .Take(Math.Max(1, priorityTake))
                .Select(result =>
                {
                    lastSuccessfulPaymentByMember.TryGetValue(result.MemberUserId, out var lastPaymentUtc);
                    overdueStatsByMember.TryGetValue(result.MemberUserId, out var overdueStats);

                    return new FinanceAiPriorityMemberItemDto
                    {
                        MemberUserId = result.MemberUserId,
                        MemberEmail = memberEmailsById.TryGetValue(result.MemberUserId, out var email)
                            ? email
                            : result.MemberUserId,
                        RiskLevel = result.RiskLevel,
                        RiskScore = result.RiskScore,
                        ReasonSummary = result.ReasonSummary,
                        LastSuccessfulPaymentUtc = lastPaymentUtc,
                        OverdueInvoiceCount = overdueStats.Count,
                        OverdueAmount = overdueStats.Amount,
                        SuggestedAction = ResolveSuggestedAction(result.RiskLevel, overdueStats.Count)
                    };
                })
                .ToList();

            return new BranchAiContext(
                new FinanceAiOverviewDto
                {
                    BranchId = branchId,
                    GeneratedAtUtc = utcNow,
                    FromUtc = normalizedFromUtc,
                    ToUtc = normalizedToUtc,
                    ScopedMemberCount = scopedMemberIds.Count,
                    HighRiskCount = highRiskCount,
                    MediumRiskCount = mediumRiskCount,
                    OverdueMemberCount = overdueStatsByMember.Count,
                    OpenInvoiceCount = openInvoiceCount,
                    OpenInvoiceExposureAmount = openInvoiceExposureAmount,
                    RenewalsDueIn30DaysCount = renewalsDueCount,
                    PriorityMembers = prioritizedAtRiskMembers
                });
        }

        private async Task<bool> SendHighRiskMemberAlertIfDueAsync(
            FinanceAiPriorityMemberItemDto member,
            string branchId,
            string trigger,
            CancellationToken cancellationToken)
        {
            var nowUtc = DateTime.UtcNow;
            var memberTrigger = BuildMemberTrigger(member.MemberUserId);
            var cooldownMinutes = Math.Max(5, _financeAlertOptions.CooldownMinutes);

            var lastMemberAlert = await _db.FinanceAlertLogs
                .AsNoTracking()
                .Where(log =>
                    log.AlertType == HighChurnAlertType &&
                    log.Trigger == memberTrigger)
                .OrderByDescending(log => log.CreatedUtc)
                .Select(log => new
                {
                    log.CreatedUtc,
                    log.State
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (lastMemberAlert is not null)
            {
                var isStillOpen =
                    lastMemberAlert.State == FinanceAlertState.New ||
                    lastMemberAlert.State == FinanceAlertState.Acknowledged;
                if (isStillOpen)
                {
                    return false;
                }

                if (nowUtc - lastMemberAlert.CreatedUtc < TimeSpan.FromMinutes(cooldownMinutes))
                {
                    return false;
                }
            }

            var payload = new
            {
                member.MemberUserId,
                member.MemberEmail,
                member.RiskLevel,
                member.RiskScore,
                member.ReasonSummary,
                member.OverdueInvoiceCount,
                member.OverdueAmount,
                member.SuggestedAction,
                branchId,
                Trigger = trigger
            };

            var message = $"High churn risk member detected: {member.MemberEmail} (score {member.RiskScore}).";
            var realtimePublished = false;
            var emailAttempted = false;
            var emailSucceeded = false;

            try
            {
                await _erpEventPublisher.PublishToRoleAsync(
                    "Finance",
                    "finance.alert",
                    message,
                    payload,
                    cancellationToken);
                await _erpEventPublisher.PublishToBackOfficeAsync(
                    "finance.alert",
                    message,
                    payload,
                    cancellationToken);
                realtimePublished = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish churn alert for member {MemberUserId}.", member.MemberUserId);
            }

            var recipients = (_financeAlertOptions.EmailRecipients ?? Array.Empty<string>())
                .Select(recipient => recipient?.Trim())
                .Where(recipient => !string.IsNullOrWhiteSpace(recipient))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (_financeAlertOptions.EmailEnabled && recipients.Length > 0)
            {
                emailAttempted = true;
                var subject = "[EJC Finance Alert] High Churn Risk Member";
                var html = $"<p><strong>{message}</strong></p>" +
                           $"<p>Branch: {branchId}<br/>Member: {member.MemberEmail}<br/>Reason: {member.ReasonSummary}<br/>UTC: {nowUtc:yyyy-MM-dd HH:mm:ss}</p>";
                var sentCount = 0;

                foreach (var recipient in recipients)
                {
                    try
                    {
                        await _emailSender.SendEmailAsync(recipient!, subject, html);
                        sentCount += 1;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to send churn alert email to {Recipient}.", recipient);
                    }
                }

                emailSucceeded = sentCount > 0;
            }

            _db.FinanceAlertLogs.Add(new FinanceAlertLog
            {
                AlertType = HighChurnAlertType,
                Trigger = memberTrigger,
                Severity = "High",
                Message = message,
                RealtimePublished = realtimePublished,
                EmailAttempted = emailAttempted,
                EmailSucceeded = emailSucceeded,
                PayloadJson = JsonSerializer.Serialize(payload),
                State = FinanceAlertState.New,
                StateUpdatedUtc = nowUtc,
                CreatedUtc = nowUtc
            });

            await _db.SaveChangesAsync(cancellationToken);
            return realtimePublished || emailSucceeded;
        }

        private static string ResolveSuggestedAction(string riskLevel, int overdueInvoiceCount)
        {
            if (overdueInvoiceCount > 0)
            {
                return "Start collection follow-up and propose payment arrangement.";
            }

            if (string.Equals(riskLevel, "High", StringComparison.OrdinalIgnoreCase))
            {
                return "Prioritize retention outreach with renewal incentive.";
            }

            return "Queue reminder campaign before renewal date.";
        }

        private async Task<IReadOnlyList<string>> ResolveScopedMemberIdsAsync(string branchId, CancellationToken cancellationToken)
        {
            return await (
                from claim in _db.UserClaims.AsNoTracking()
                join userRole in _db.UserRoles.AsNoTracking() on claim.UserId equals userRole.UserId
                join role in _db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                where claim.ClaimType == BranchAccess.BranchIdClaimType &&
                      claim.ClaimValue == branchId &&
                      role.Name != null &&
                      role.Name == "Member"
                select claim.UserId)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        private async Task<IReadOnlyList<string>> ResolveMemberBranchIdsAsync(CancellationToken cancellationToken)
        {
            return await (
                from claim in _db.UserClaims.AsNoTracking()
                join userRole in _db.UserRoles.AsNoTracking() on claim.UserId equals userRole.UserId
                join role in _db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                where claim.ClaimType == BranchAccess.BranchIdClaimType &&
                      claim.ClaimValue != null &&
                      role.Name != null &&
                      role.Name == "Member"
                select claim.ClaimValue!)
                .Distinct()
                .OrderBy(value => value)
                .ToListAsync(cancellationToken);
        }

        private static string BuildMemberTrigger(string memberUserId)
        {
            var normalized = (memberUserId ?? string.Empty).Trim();
            if (normalized.Length > 68)
            {
                normalized = normalized[..68];
            }

            return $"churn-high:{normalized}";
        }

        private static DateTime? NormalizeToUtc(DateTime? value)
        {
            if (!value.HasValue)
            {
                return null;
            }

            var raw = value.Value;
            if (raw.Kind == DateTimeKind.Utc)
            {
                return raw;
            }

            return raw.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(raw, DateTimeKind.Utc)
                : raw.ToUniversalTime();
        }

        private sealed record BranchAiContext(FinanceAiOverviewDto Overview);
    }
}
