using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Controllers
{
    [Authorize(Roles = "Staff,Admin,Finance,SuperAdmin")]
    public class InvoicesController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;

        public InvoicesController(ApplicationDbContext db, UserManager<IdentityUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index(InvoiceStatus? status)
        {
            var query = _db.Invoices.AsQueryable();
            if (status.HasValue)
            {
                query = query.Where(i => i.Status == status.Value);
            }

            var invoices = await query
                .Include(i => i.Payments)
                .OrderByDescending(i => i.IssueDateUtc)
                .Take(200)
                .ToListAsync();

            ViewBag.Status = status;
            return View(invoices);
        }

        [Authorize(Roles = "Staff,Admin,Finance,SuperAdmin")]
        public async Task<IActionResult> Create()
        {
            var members = await _userManager.GetUsersInRoleAsync("Member");
            ViewBag.MemberUserId = new SelectList(members.OrderBy(u => u.Email), "Id", "Email");

            return View(new Invoice
            {
                IssueDateUtc = DateTime.UtcNow,
                DueDateUtc = DateTime.UtcNow.AddDays(7),
                Status = InvoiceStatus.Unpaid
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Staff,Admin,Finance,SuperAdmin")]
        public async Task<IActionResult> Create(Invoice invoice)
        {
            if (!ModelState.IsValid)
            {
                var members = await _userManager.GetUsersInRoleAsync("Member");
                ViewBag.MemberUserId = new SelectList(members.OrderBy(u => u.Email), "Id", "Email", invoice.MemberUserId);
                return View(invoice);
            }

            invoice.InvoiceNumber = GenerateInvoiceNumber();
            invoice.IssueDateUtc = DateTime.SpecifyKind(invoice.IssueDateUtc, DateTimeKind.Utc);
            invoice.DueDateUtc = DateTime.SpecifyKind(invoice.DueDateUtc, DateTimeKind.Utc);

            _db.Invoices.Add(invoice);
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Details), new { id = invoice.Id });
        }

        public async Task<IActionResult> Details(int id)
        {
            var invoice = await _db.Invoices
                .Include(i => i.Payments)
                .FirstOrDefaultAsync(i => i.Id == id);

            if (invoice is null)
            {
                return NotFound();
            }

            return View(invoice);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddPayment(int id, decimal amount, PaymentMethod method, string? referenceNumber)
        {
            var invoice = await _db.Invoices
                .Include(i => i.Payments)
                .FirstOrDefaultAsync(i => i.Id == id);

            if (invoice is null)
            {
                return NotFound();
            }

            if (amount <= 0)
            {
                ModelState.AddModelError(string.Empty, "Amount must be greater than 0.");
                return RedirectToAction(nameof(Details), new { id });
            }

            var payment = new Payment
            {
                InvoiceId = invoice.Id,
                Amount = amount,
                Method = method,
                Status = PaymentStatus.Succeeded,
                PaidAtUtc = DateTime.UtcNow,
                ReferenceNumber = referenceNumber,
                ReceivedByUserId = _userManager.GetUserId(User),
                GatewayProvider = method == PaymentMethod.OnlineGateway ? "Gateway" : null,
                GatewayPaymentId = method == PaymentMethod.OnlineGateway ? referenceNumber : null,
            };

            _db.Payments.Add(payment);

            var totalPaid = invoice.Payments.Sum(p => p.Amount) + amount;
            if (totalPaid >= invoice.Amount)
            {
                invoice.Status = InvoiceStatus.Paid;
            }

            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Details), new { id });
        }

        private static string GenerateInvoiceNumber()
        {
            // Simple unique-enough format for now; can be replaced with per-branch sequences later.
            return $"INV-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        }
    }
}
