using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Pages.Member
{
    [Authorize(Roles = "Member")]
    public class PaymentMethodsModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IAutoBillingService _autoBillingService;

        public PaymentMethodsModel(
            ApplicationDbContext db,
            UserManager<IdentityUser> userManager,
            IAutoBillingService autoBillingService)
        {
            _db = db;
            _userManager = userManager;
            _autoBillingService = autoBillingService;
        }

        public IReadOnlyList<SavedPaymentMethodViewModel> SavedMethods { get; private set; } = Array.Empty<SavedPaymentMethodViewModel>();
        public bool HasActiveSubscription { get; private set; }
        public string? NextBillingDate { get; private set; }
        public decimal? NextBillingAmount { get; private set; }

        [TempData]
        public string? StatusMessage { get; set; }

        public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Challenge();
            }

            await LoadDataAsync(userId, cancellationToken);
            return Page();
        }

        public async Task<IActionResult> OnPostToggleAutoBillingAsync(int methodId, CancellationToken cancellationToken)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Challenge();
            }

            var method = await _db.SavedPaymentMethods
                .FirstOrDefaultAsync(m => m.Id == methodId && m.MemberUserId == userId, cancellationToken);

            if (method is null)
            {
                StatusMessage = "Payment method not found.";
                return RedirectToPage();
            }

            method.AutoBillingEnabled = !method.AutoBillingEnabled;
            await _db.SaveChangesAsync(cancellationToken);

            StatusMessage = method.AutoBillingEnabled
                ? "Auto-pay has been enabled. Your subscription will renew automatically."
                : "Auto-pay has been disabled. You will need to pay manually when your subscription is due.";

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostSetDefaultAsync(int methodId, CancellationToken cancellationToken)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Challenge();
            }

            var methods = await _db.SavedPaymentMethods
                .Where(m => m.MemberUserId == userId)
                .ToListAsync(cancellationToken);

            var target = methods.FirstOrDefault(m => m.Id == methodId);
            if (target is null)
            {
                StatusMessage = "Payment method not found.";
                return RedirectToPage();
            }

            foreach (var m in methods)
            {
                m.IsDefault = m.Id == methodId;
            }

            await _db.SaveChangesAsync(cancellationToken);
            StatusMessage = "Default payment method updated.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostRemoveAsync(int methodId, CancellationToken cancellationToken)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Challenge();
            }

            var method = await _db.SavedPaymentMethods
                .FirstOrDefaultAsync(m => m.Id == methodId && m.MemberUserId == userId, cancellationToken);

            if (method is null)
            {
                StatusMessage = "Payment method not found.";
                return RedirectToPage();
            }

            // Soft delete - just mark as inactive
            method.IsActive = false;
            method.AutoBillingEnabled = false;
            await _db.SaveChangesAsync(cancellationToken);

            StatusMessage = "Payment method removed.";
            return RedirectToPage();
        }

        private async Task LoadDataAsync(string userId, CancellationToken cancellationToken)
        {
            // Get saved payment methods
            var methods = await _db.SavedPaymentMethods
                .Where(m => m.MemberUserId == userId && m.IsActive)
                .OrderByDescending(m => m.IsDefault)
                .ThenByDescending(m => m.CreatedUtc)
                .ToListAsync(cancellationToken);

            SavedMethods = methods.Select(m => new SavedPaymentMethodViewModel
            {
                Id = m.Id,
                DisplayLabel = m.DisplayLabel ?? GetDefaultLabel(m.PaymentMethodType),
                PaymentMethodType = m.PaymentMethodType,
                IsDefault = m.IsDefault,
                AutoBillingEnabled = m.AutoBillingEnabled,
                LastUsedUtc = m.LastUsedUtc,
                FailedAttempts = m.FailedAttempts,
                CreatedUtc = m.CreatedUtc
            }).ToList();

            // Get subscription info
            var activeSubscription = await _db.MemberSubscriptions
                .Include(s => s.SubscriptionPlan)
                .Where(s => s.MemberUserId == userId && s.Status == SubscriptionStatus.Active)
                .OrderByDescending(s => s.EndDateUtc)
                .FirstOrDefaultAsync(cancellationToken);

            HasActiveSubscription = activeSubscription is not null;

            if (activeSubscription?.EndDateUtc.HasValue == true)
            {
                NextBillingDate = activeSubscription.EndDateUtc.Value.ToLocalTime().ToString("MMMM dd, yyyy");
                NextBillingAmount = activeSubscription.SubscriptionPlan?.Price;
            }

            // Or check for unpaid invoices
            var nextInvoice = await _db.Invoices
                .Where(i => i.MemberUserId == userId &&
                           (i.Status == InvoiceStatus.Unpaid || i.Status == InvoiceStatus.Overdue))
                .OrderBy(i => i.DueDateUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (nextInvoice is not null)
            {
                NextBillingDate = nextInvoice.DueDateUtc.ToLocalTime().ToString("MMMM dd, yyyy");
                NextBillingAmount = nextInvoice.Amount;
            }
        }

        private static string GetDefaultLabel(string type)
        {
            return type.ToLowerInvariant() switch
            {
                "card" => "Credit/Debit Card",
                "gcash" => "GCash",
                "grab_pay" => "GrabPay",
                "paymaya" => "PayMaya",
                _ => type
            };
        }

        public class SavedPaymentMethodViewModel
        {
            public int Id { get; set; }
            public string DisplayLabel { get; set; } = string.Empty;
            public string PaymentMethodType { get; set; } = string.Empty;
            public bool IsDefault { get; set; }
            public bool AutoBillingEnabled { get; set; }
            public DateTime? LastUsedUtc { get; set; }
            public int FailedAttempts { get; set; }
            public DateTime CreatedUtc { get; set; }
        }
    }
}
