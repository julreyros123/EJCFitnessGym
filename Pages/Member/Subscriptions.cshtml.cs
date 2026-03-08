using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Services.Memberships;
using EJCFitnessGym.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Pages.Member
{
    [Authorize(Roles = "Member")]
    public class SubscriptionsModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;

        public SubscriptionsModel(ApplicationDbContext db, UserManager<IdentityUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public MemberSubscription? CurrentSubscription { get; set; }
        public List<MemberSubscription> SubscriptionHistory { get; set; } = new();
        public List<SubscriptionPlan> AvailablePlans { get; set; } = new();
        public string? StatusMessage { get; set; }
        public int? DaysRemaining { get; set; }
        public bool IsExpiringSoon { get; set; }
        public decimal OutstandingAmount { get; set; }
        public DateTime? NextDueDateUtc { get; set; }
        public int ActiveSavedPaymentMethodCount { get; set; }
        public bool AutomaticRenewalAvailable => PayMongoBillingCapabilities.SupportsOffSessionAutoBilling;

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound();
            }

            CurrentSubscription = await _db.MemberSubscriptions
                .Include(s => s.SubscriptionPlan)
                .Where(s => s.MemberUserId == user.Id)
                .OrderByDescending(s => s.StartDateUtc)
                .FirstOrDefaultAsync();

            SubscriptionHistory = await _db.MemberSubscriptions
                .Include(s => s.SubscriptionPlan)
                .Where(s => s.MemberUserId == user.Id)
                .OrderByDescending(s => s.StartDateUtc)
                .Take(10)
                .ToListAsync();

            AvailablePlans = await _db.SubscriptionPlans
                .Where(p => p.IsActive)
                .OrderBy(p => p.Price)
                .ToListAsync();

            if (CurrentSubscription?.EndDateUtc.HasValue == true)
            {
                DaysRemaining = (CurrentSubscription.EndDateUtc.Value.Date - DateTime.UtcNow.Date).Days;
                IsExpiringSoon = DaysRemaining <= 7;
            }

            var openInvoices = await _db.Invoices
                .Where(i =>
                    i.MemberUserId == user.Id &&
                    (i.Status == InvoiceStatus.Unpaid || i.Status == InvoiceStatus.Overdue))
                .OrderBy(i => i.DueDateUtc)
                .ToListAsync();

            OutstandingAmount = openInvoices.Sum(i => i.Amount);
            NextDueDateUtc = openInvoices.FirstOrDefault()?.DueDateUtc;

            ActiveSavedPaymentMethodCount = await _db.SavedPaymentMethods
                .CountAsync(method => method.MemberUserId == user.Id && method.IsActive);

            // Navigation notification data
            var overdueCount = openInvoices.Count(i => i.Status == InvoiceStatus.Overdue);
            ViewData["OverdueInvoiceCount"] = overdueCount;

            return Page();
        }

        public string GetStatusBadgeClass(SubscriptionStatus status)
        {
            return status switch
            {
                SubscriptionStatus.Active => "bg-success",
                SubscriptionStatus.Expired => "bg-danger",
                SubscriptionStatus.Paused => "bg-warning",
                SubscriptionStatus.Cancelled => "bg-secondary",
                _ => "bg-secondary"
            };
        }

        public string GetBillingCycleText(BillingCycle cycle)
        {
            return cycle switch
            {
                BillingCycle.Monthly => "Monthly",
                BillingCycle.Weekly => "Weekly",
                BillingCycle.Yearly => "Yearly",
                _ => cycle.ToString()
            };
        }

        public IReadOnlyList<string> GetPlanBenefits(SubscriptionPlan? plan)
        {
            return plan is null
                ? Array.Empty<string>()
                : SubscriptionPlanCatalog.BuildBenefits(plan);
        }
    }
}
