using System.Security.Claims;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Models;
using EJCFitnessGym.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Pages.Member
{
    [Authorize(Policy = "MemberAccess")]
    public class DashboardModel : PageModel
    {
        private readonly ApplicationDbContext _db;

        public DashboardModel(ApplicationDbContext db)
        {
            _db = db;
        }

        public string MemberAccountNumber { get; private set; } = string.Empty;
        public string MemberDisplayName { get; private set; } = "Member";
        public string WelcomeName { get; private set; } = "Member";

        public string MemberQrPayload =>
            string.IsNullOrWhiteSpace(MemberAccountNumber)
                ? string.Empty
                : $"EJC-MEMBER:{MemberAccountNumber}";

        public MemberSubscription? CurrentSubscription { get; private set; }
        public int? DaysRemaining { get; private set; }
        public decimal TotalOutstanding { get; private set; }
        public int OutstandingInvoiceCount { get; private set; }
        public DateTime? NextDueDateUtc { get; private set; }
        public decimal? NextDueAmount { get; private set; }
        public int RecentPaymentCount30Days { get; private set; }
        public int ActiveSavedPaymentMethods { get; private set; }
        public int ProfileCompletionPercent { get; private set; }
        public IReadOnlyList<string> ProfileMissingItems { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<DashboardInvoiceView> RecentInvoices { get; private set; } = Array.Empty<DashboardInvoiceView>();
        public bool AutomaticRenewalAvailable => PayMongoBillingCapabilities.SupportsOffSessionAutoBilling;

        public string SubscriptionStatusLabel =>
            CurrentSubscription?.Status.ToString() ?? "No Plan";

        public bool HasOutstandingBalance => TotalOutstanding > 0m;

        public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Challenge();
            }

            MemberAccountNumber = userId;
            MemberDisplayName = User.Identity?.Name?.Trim() ?? "Member";

            var profile = await _db.MemberProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

            var resolvedDisplayName = ResolveDisplayName(profile, MemberDisplayName);
            MemberDisplayName = resolvedDisplayName;
            WelcomeName = ResolveWelcomeName(profile, resolvedDisplayName);

            CurrentSubscription = await _db.MemberSubscriptions
                .AsNoTracking()
                .Include(s => s.SubscriptionPlan)
                .Where(s => s.MemberUserId == userId)
                .OrderByDescending(s => s.StartDateUtc)
                .ThenByDescending(s => s.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (CurrentSubscription?.EndDateUtc.HasValue == true)
            {
                DaysRemaining = (CurrentSubscription.EndDateUtc.Value.Date - DateTime.UtcNow.Date).Days;
            }

            var openInvoicesQuery = _db.Invoices
                .AsNoTracking()
                .Where(i =>
                    i.MemberUserId == userId &&
                    (i.Status == InvoiceStatus.Unpaid || i.Status == InvoiceStatus.Overdue));

            TotalOutstanding = await openInvoicesQuery.SumAsync(i => (decimal?)i.Amount, cancellationToken) ?? 0m;
            OutstandingInvoiceCount = await openInvoicesQuery.CountAsync(cancellationToken);

            var nextDueInvoice = await openInvoicesQuery
                .OrderBy(i => i.DueDateUtc)
                .Select(i => new
                {
                    i.DueDateUtc,
                    i.Amount
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (nextDueInvoice is not null)
            {
                NextDueDateUtc = nextDueInvoice.DueDateUtc;
                NextDueAmount = nextDueInvoice.Amount;
            }

            var paymentCutoffUtc = DateTime.UtcNow.AddDays(-30);
            RecentPaymentCount30Days = await
                (from payment in _db.Payments.AsNoTracking()
                 join invoice in _db.Invoices.AsNoTracking() on payment.InvoiceId equals invoice.Id
                 where invoice.MemberUserId == userId &&
                       payment.Status == PaymentStatus.Succeeded &&
                       payment.PaidAtUtc >= paymentCutoffUtc
                 select payment.Id).CountAsync(cancellationToken);

            ActiveSavedPaymentMethods = await _db.SavedPaymentMethods
                .AsNoTracking()
                .CountAsync(method =>
                    method.MemberUserId == userId &&
                    method.IsActive,
                    cancellationToken);

            var recentInvoices = await _db.Invoices
                .AsNoTracking()
                .Where(i => i.MemberUserId == userId)
                .OrderByDescending(i => i.IssueDateUtc)
                .Take(5)
                .Select(i => new DashboardInvoiceView
                {
                    Id = i.Id,
                    InvoiceNumber = i.InvoiceNumber,
                    IssueDateUtc = i.IssueDateUtc,
                    DueDateUtc = i.DueDateUtc,
                    Amount = i.Amount,
                    Status = i.Status
                })
                .ToListAsync(cancellationToken);

            RecentInvoices = recentInvoices;
            BuildProfileCompleteness(profile);

            // Pass notification data to nav partial
            var overdueCount = await _db.Invoices
                .AsNoTracking()
                .CountAsync(i =>
                    i.MemberUserId == userId &&
                    i.Status == InvoiceStatus.Overdue, cancellationToken);

            ViewData["OverdueInvoiceCount"] = overdueCount;
            ViewData["ProfileCompletionPercent"] = ProfileCompletionPercent;

            return Page();
        }

        public string GetSubscriptionBadgeClass()
        {
            return CurrentSubscription?.Status switch
            {
                SubscriptionStatus.Active => "bg-success-subtle text-success-emphasis border border-success-subtle",
                SubscriptionStatus.Expired => "bg-danger-subtle text-danger-emphasis border border-danger-subtle",
                SubscriptionStatus.Paused => "bg-warning-subtle text-warning-emphasis border border-warning-subtle",
                SubscriptionStatus.Cancelled => "bg-secondary",
                _ => "bg-secondary"
            };
        }

        public string GetInvoiceStatusBadgeClass(InvoiceStatus status)
        {
            return status switch
            {
                InvoiceStatus.Paid => "bg-success",
                InvoiceStatus.Unpaid => "bg-warning text-dark",
                InvoiceStatus.Overdue => "bg-danger",
                InvoiceStatus.Voided => "bg-secondary",
                _ => "bg-secondary"
            };
        }

        private void BuildProfileCompleteness(MemberProfile? profile)
        {
            var checks = new List<(bool Completed, string Label)>
            {
                (!string.IsNullOrWhiteSpace(profile?.FirstName), "First name"),
                (!string.IsNullOrWhiteSpace(profile?.LastName), "Last name"),
                (!string.IsNullOrWhiteSpace(profile?.PhoneNumber), "Phone number"),
                (profile?.Age.HasValue == true, "Age"),
                (profile?.HeightCm.HasValue == true, "Height"),
                (profile?.WeightKg.HasValue == true, "Weight")
            };

            var totalChecks = checks.Count;
            var completedChecks = checks.Count(check => check.Completed);
            ProfileCompletionPercent = (int)Math.Round((completedChecks / (decimal)totalChecks) * 100m);

            ProfileMissingItems = checks
                .Where(check => !check.Completed)
                .Select(check => check.Label)
                .ToList();
        }

        private static string ResolveDisplayName(MemberProfile? profile, string fallback)
        {
            var fullName = string.Join(
                " ",
                new[] { profile?.FirstName, profile?.LastName }
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value!.Trim()));

            return string.IsNullOrWhiteSpace(fullName) ? fallback : fullName;
        }

        private static string ResolveWelcomeName(MemberProfile? profile, string fallbackDisplayName)
        {
            if (!string.IsNullOrWhiteSpace(profile?.FirstName))
            {
                return profile.FirstName.Trim();
            }

            var firstToken = fallbackDisplayName
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
            return string.IsNullOrWhiteSpace(firstToken) ? "Member" : firstToken;
        }

        public sealed class DashboardInvoiceView
        {
            public int Id { get; init; }
            public string InvoiceNumber { get; init; } = string.Empty;
            public DateTime IssueDateUtc { get; init; }
            public DateTime DueDateUtc { get; init; }
            public decimal Amount { get; init; }
            public InvoiceStatus Status { get; init; }
        }
    }
}
