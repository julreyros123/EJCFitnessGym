using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Pages.Member
{
    [Authorize(Roles = "Member")]
    public class PaymentsModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;

        public PaymentsModel(ApplicationDbContext db, UserManager<IdentityUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public List<InvoiceView> Invoices { get; set; } = new();
        public decimal TotalOutstanding { get; set; }
        public decimal TotalPaid { get; set; }
        public int OpenInvoiceCount { get; set; }
        public int OverdueInvoiceCount { get; set; }
        public int DueSoonInvoiceCount { get; set; }
        public InvoiceView? NextActionInvoice { get; set; }

        public class InvoiceView
        {
            public int Id { get; set; }
            public string InvoiceNumber { get; set; } = string.Empty;
            public DateTime IssueDateUtc { get; set; }
            public DateTime DueDateUtc { get; set; }
            public decimal Amount { get; set; }
            public InvoiceStatus Status { get; set; }
            public string? Notes { get; set; }
            public List<PaymentView> Payments { get; set; } = new();
        }

        public class PaymentView
        {
            public int Id { get; set; }
            public DateTime PaidAtUtc { get; set; }
            public decimal Amount { get; set; }
            public PaymentMethod Method { get; set; }
            public PaymentStatus Status { get; set; }
            public string? ReferenceNumber { get; set; }
        }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound();
            }

            var invoices = await _db.Invoices
                .Include(i => i.Payments)
                .Include(i => i.MemberSubscription)
                    .ThenInclude(s => s!.SubscriptionPlan)
                .Where(i => i.MemberUserId == user.Id)
                .OrderByDescending(i => i.IssueDateUtc)
                .ToListAsync();

            Invoices = invoices.Select(i => new InvoiceView
            {
                Id = i.Id,
                InvoiceNumber = i.InvoiceNumber,
                IssueDateUtc = i.IssueDateUtc,
                DueDateUtc = i.DueDateUtc,
                Amount = i.Amount,
                Status = i.Status,
                Notes = i.Notes,
                Payments = i.Payments.Select(p => new PaymentView
                {
                    Id = p.Id,
                    PaidAtUtc = p.PaidAtUtc,
                    Amount = p.Amount,
                    Method = p.Method,
                    Status = p.Status,
                    ReferenceNumber = p.ReferenceNumber
                }).ToList()
            }).ToList();

            TotalOutstanding = invoices
                .Where(i => i.Status == InvoiceStatus.Unpaid || i.Status == InvoiceStatus.Overdue)
                .Sum(i => i.Amount);

            TotalPaid = invoices
                .Where(i => i.Status == InvoiceStatus.Paid)
                .Sum(i => i.Amount);

            var todayUtc = DateTime.UtcNow.Date;
            var openInvoices = Invoices
                .Where(i => i.Status == InvoiceStatus.Unpaid || i.Status == InvoiceStatus.Overdue)
                .OrderBy(i => i.DueDateUtc)
                .ToList();

            OpenInvoiceCount = openInvoices.Count;
            OverdueInvoiceCount = openInvoices.Count(i => i.DueDateUtc.Date < todayUtc);
            DueSoonInvoiceCount = openInvoices.Count(i => i.DueDateUtc.Date >= todayUtc && i.DueDateUtc.Date <= todayUtc.AddDays(3));
            NextActionInvoice = openInvoices.FirstOrDefault();

            ViewData["OverdueInvoiceCount"] = OverdueInvoiceCount;

            return Page();
        }

        public string GetStatusBadgeClass(InvoiceStatus status)
        {
            return status switch
            {
                InvoiceStatus.Paid => "bg-success",
                InvoiceStatus.Unpaid => "bg-warning",
                InvoiceStatus.Overdue => "bg-danger",
                InvoiceStatus.Voided => "bg-secondary",
                _ => "bg-secondary"
            };
        }

        public string GetPaymentMethodText(PaymentMethod method)
        {
            return method switch
            {
                PaymentMethod.Cash => "Cash",
                PaymentMethod.OnlineGateway => "Online Payment",
                PaymentMethod.BankTransfer => "Bank Transfer",
                PaymentMethod.Card => "Card",
                PaymentMethod.EWallet => "E-Wallet",
                _ => method.ToString()
            };
        }

        public string GetPaymentStatusBadgeClass(PaymentStatus status)
        {
            return status switch
            {
                PaymentStatus.Succeeded => "bg-success",
                PaymentStatus.Pending => "bg-warning",
                PaymentStatus.Failed => "bg-danger",
                _ => "bg-secondary"
            };
        }
    }
}
