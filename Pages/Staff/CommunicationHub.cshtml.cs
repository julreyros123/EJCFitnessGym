using System.Security.Claims;
using System.Text.Json;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Security;
using EJCFitnessGym.Services.Integration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Pages.Staff
{
    public class CommunicationHubModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IIntegrationOutbox _integrationOutbox;

        public CommunicationHubModel(ApplicationDbContext db, IIntegrationOutbox integrationOutbox)
        {
            _db = db;
            _integrationOutbox = integrationOutbox;
        }

        [TempData]
        public string? StatusMessage { get; set; }

        public string ScopeLabel { get; private set; } = "All Branches";
        public int DueSoonInvoiceCount { get; private set; }
        public int OverdueInvoiceCount { get; private set; }
        public IReadOnlyList<InvoiceReminderRow> InvoiceReminders { get; private set; } = Array.Empty<InvoiceReminderRow>();
        public IReadOnlyList<ExpiringPlanRow> ExpiringPlans { get; private set; } = Array.Empty<ExpiringPlanRow>();

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            await LoadAsync(cancellationToken);
        }

        public async Task<IActionResult> OnPostQueueReminderAsync(int invoiceId, CancellationToken cancellationToken)
        {
            var branchId = User.IsInRole("SuperAdmin") ? null : User.GetBranchId();

            var invoiceQuery = _db.Invoices
                .AsNoTracking()
                .Where(invoice => invoice.Id == invoiceId);

            if (!string.IsNullOrWhiteSpace(branchId))
            {
                invoiceQuery = invoiceQuery.Where(invoice => invoice.BranchId == branchId);
            }

            var invoice = await invoiceQuery.FirstOrDefaultAsync(cancellationToken);
            if (invoice is null)
            {
                StatusMessage = "Invoice not found for your current branch scope.";
                return RedirectToPage();
            }

            var profile = await _db.MemberProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(member => member.UserId == invoice.MemberUserId, cancellationToken);

            var memberDisplayName = BuildDisplayName(profile) ?? invoice.MemberUserId;
            var queuedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            await _integrationOutbox.EnqueueBackOfficeAsync(
                eventType: "billing.invoice.reminder",
                message: $"Reminder queued for invoice {invoice.InvoiceNumber}.",
                data: new
                {
                    invoiceId = invoice.Id,
                    invoiceNumber = invoice.InvoiceNumber,
                    memberUserId = invoice.MemberUserId,
                    memberDisplayName,
                    branchId = invoice.BranchId,
                    amount = invoice.Amount,
                    dueDateUtc = invoice.DueDateUtc,
                    queuedByUserId,
                    queuedAtUtc = DateTime.UtcNow
                },
                cancellationToken: cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
            StatusMessage = $"Reminder queued for {memberDisplayName}.";
            return RedirectToPage();
        }

        private async Task LoadAsync(CancellationToken cancellationToken)
        {
            var branchId = User.IsInRole("SuperAdmin") ? null : User.GetBranchId();
            ScopeLabel = string.IsNullOrWhiteSpace(branchId)
                ? "All Branches"
                : $"Branch {branchId}";

            var todayUtc = DateTime.UtcNow.Date;
            var invoicesQuery = _db.Invoices
                .AsNoTracking()
                .Where(invoice =>
                    (invoice.Status == InvoiceStatus.Unpaid || invoice.Status == InvoiceStatus.Overdue) &&
                    invoice.DueDateUtc.Date <= todayUtc.AddDays(3));

            if (!string.IsNullOrWhiteSpace(branchId))
            {
                invoicesQuery = invoicesQuery.Where(invoice => invoice.BranchId == branchId);
            }

            var invoices = await invoicesQuery
                .OrderBy(invoice => invoice.DueDateUtc)
                .ThenBy(invoice => invoice.InvoiceNumber)
                .Take(120)
                .ToListAsync(cancellationToken);

            DueSoonInvoiceCount = invoices.Count(invoice => invoice.DueDateUtc.Date >= todayUtc);
            OverdueInvoiceCount = invoices.Count(invoice => invoice.DueDateUtc.Date < todayUtc);

            var memberUserIds = invoices
                .Select(invoice => invoice.MemberUserId)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var memberProfiles = await _db.MemberProfiles
                .AsNoTracking()
                .Where(profile => memberUserIds.Contains(profile.UserId))
                .ToDictionaryAsync(profile => profile.UserId, profile => profile, StringComparer.Ordinal, cancellationToken);

            var lastReminderByInvoiceId = await BuildLastReminderMapAsync(cancellationToken);

            InvoiceReminders = invoices.Select(invoice =>
            {
                memberProfiles.TryGetValue(invoice.MemberUserId, out var profile);
                var memberDisplayName = BuildDisplayName(profile) ?? invoice.MemberUserId;
                var daysFromDueDate = (invoice.DueDateUtc.Date - todayUtc).Days;
                lastReminderByInvoiceId.TryGetValue(invoice.Id, out var lastReminderUtc);

                return new InvoiceReminderRow
                {
                    InvoiceId = invoice.Id,
                    InvoiceNumber = invoice.InvoiceNumber,
                    MemberUserId = invoice.MemberUserId,
                    MemberDisplayName = memberDisplayName,
                    DueDateUtc = invoice.DueDateUtc,
                    DaysFromDueDate = daysFromDueDate,
                    Amount = invoice.Amount,
                    LastReminderUtc = lastReminderUtc
                };
            }).ToList();

            var expiringSubscriptions = await _db.MemberSubscriptions
                .AsNoTracking()
                .Include(subscription => subscription.SubscriptionPlan)
                .Where(subscription =>
                    subscription.Status == SubscriptionStatus.Active &&
                    subscription.EndDateUtc.HasValue &&
                    subscription.EndDateUtc.Value.Date >= todayUtc &&
                    subscription.EndDateUtc.Value.Date <= todayUtc.AddDays(7))
                .OrderBy(subscription => subscription.EndDateUtc)
                .Take(100)
                .ToListAsync(cancellationToken);

            var expiringMemberIds = expiringSubscriptions
                .Select(subscription => subscription.MemberUserId)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var expiringMemberProfiles = await _db.MemberProfiles
                .AsNoTracking()
                .Where(profile => expiringMemberIds.Contains(profile.UserId))
                .ToDictionaryAsync(profile => profile.UserId, profile => profile, StringComparer.Ordinal, cancellationToken);

            ExpiringPlans = expiringSubscriptions.Select(subscription =>
            {
                expiringMemberProfiles.TryGetValue(subscription.MemberUserId, out var profile);
                var memberDisplayName = BuildDisplayName(profile) ?? subscription.MemberUserId;
                var endDateUtc = subscription.EndDateUtc ?? DateTime.UtcNow;
                return new ExpiringPlanRow
                {
                    MemberUserId = subscription.MemberUserId,
                    MemberDisplayName = memberDisplayName,
                    PlanName = subscription.SubscriptionPlan?.Name ?? $"Plan #{subscription.SubscriptionPlanId}",
                    EndDateUtc = endDateUtc,
                    DaysToEnd = Math.Max(0, (endDateUtc.Date - todayUtc).Days)
                };
            }).ToList();
        }

        private async Task<Dictionary<int, DateTime>> BuildLastReminderMapAsync(CancellationToken cancellationToken)
        {
            var reminderMessages = await _db.IntegrationOutboxMessages
                .AsNoTracking()
                .Where(message =>
                    message.EventType == "billing.invoice.reminder" &&
                    message.CreatedUtc >= DateTime.UtcNow.AddDays(-60))
                .OrderByDescending(message => message.CreatedUtc)
                .Take(300)
                .ToListAsync(cancellationToken);

            var map = new Dictionary<int, DateTime>();
            foreach (var message in reminderMessages)
            {
                var invoiceId = TryReadInvoiceId(message.PayloadJson);
                if (!invoiceId.HasValue || map.ContainsKey(invoiceId.Value))
                {
                    continue;
                }

                map[invoiceId.Value] = message.CreatedUtc;
            }

            return map;
        }

        private static int? TryReadInvoiceId(string? payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(payloadJson);
                if (!document.RootElement.TryGetProperty("invoiceId", out var invoiceIdProperty))
                {
                    return null;
                }

                if (invoiceIdProperty.ValueKind == JsonValueKind.Number &&
                    invoiceIdProperty.TryGetInt32(out var parsedNumber))
                {
                    return parsedNumber;
                }

                if (invoiceIdProperty.ValueKind == JsonValueKind.String &&
                    int.TryParse(invoiceIdProperty.GetString(), out var parsedString))
                {
                    return parsedString;
                }
            }
            catch (JsonException)
            {
                return null;
            }

            return null;
        }

        private static string? BuildDisplayName(Models.MemberProfile? profile)
        {
            if (profile is null)
            {
                return null;
            }

            var fullName = string.Join(
                " ",
                new[] { profile.FirstName, profile.LastName }
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!.Trim()));

            return string.IsNullOrWhiteSpace(fullName) ? null : fullName;
        }

        public sealed class InvoiceReminderRow
        {
            public int InvoiceId { get; init; }
            public string InvoiceNumber { get; init; } = string.Empty;
            public string MemberUserId { get; init; } = string.Empty;
            public string MemberDisplayName { get; init; } = string.Empty;
            public DateTime DueDateUtc { get; init; }
            public int DaysFromDueDate { get; init; }
            public decimal Amount { get; init; }
            public DateTime? LastReminderUtc { get; init; }
        }

        public sealed class ExpiringPlanRow
        {
            public string MemberUserId { get; init; } = string.Empty;
            public string MemberDisplayName { get; init; } = string.Empty;
            public string PlanName { get; init; } = string.Empty;
            public DateTime EndDateUtc { get; init; }
            public int DaysToEnd { get; init; }
        }
    }
}
