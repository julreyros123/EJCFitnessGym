using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Services.Memberships;
using EJCFitnessGym.Security;
using EJCFitnessGym.Services.Finance;
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
        private readonly IGeneralLedgerService _generalLedgerService;
        private readonly ILogger<InvoicesController> _logger;

        public InvoicesController(
            ApplicationDbContext db,
            UserManager<IdentityUser> userManager,
            IGeneralLedgerService generalLedgerService,
            ILogger<InvoicesController> logger)
        {
            _db = db;
            _userManager = userManager;
            _generalLedgerService = generalLedgerService;
            _logger = logger;
        }

        public async Task<IActionResult> Index(InvoiceStatus? status)
        {
            var query = ApplyBranchScope(_db.Invoices.AsQueryable());
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
            if (!User.IsInRole("SuperAdmin") && string.IsNullOrWhiteSpace(User.GetBranchId()))
            {
                return Forbid();
            }

            await PopulateMemberSelectListAsync();

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
            if (!User.IsInRole("SuperAdmin") && string.IsNullOrWhiteSpace(User.GetBranchId()))
            {
                return Forbid();
            }

            if (!await CanAccessMemberAsync(invoice.MemberUserId))
            {
                ModelState.AddModelError(nameof(invoice.MemberUserId), "Selected member is outside your branch scope.");
            }

            var memberBranchId = await ResolveMemberBranchIdAsync(invoice.MemberUserId);
            if (string.IsNullOrWhiteSpace(memberBranchId))
            {
                ModelState.AddModelError(nameof(invoice.MemberUserId), "Selected member has no branch assignment.");
            }

            if (!ModelState.IsValid)
            {
                await PopulateMemberSelectListAsync(invoice.MemberUserId);
                return View(invoice);
            }

            invoice.InvoiceNumber = GenerateInvoiceNumber();
            invoice.IssueDateUtc = DateTime.SpecifyKind(invoice.IssueDateUtc, DateTimeKind.Utc);
            invoice.DueDateUtc = DateTime.SpecifyKind(invoice.DueDateUtc, DateTimeKind.Utc);
            invoice.BranchId = memberBranchId;

            _db.Invoices.Add(invoice);
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Details), new { id = invoice.Id });
        }

        public async Task<IActionResult> Details(int id)
        {
            var invoice = await ApplyBranchScope(_db.Invoices)
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
        public async Task<IActionResult> AddPayment(
            int id,
            decimal amount,
            PaymentMethod method,
            string? referenceNumber,
            CancellationToken cancellationToken)
        {
            var invoice = await ApplyBranchScope(_db.Invoices)
                .Include(i => i.Payments)
                .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

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
                BranchId = invoice.BranchId,
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

            var totalPaid = invoice.Payments
                .Where(p => p.Status == PaymentStatus.Succeeded)
                .Sum(p => p.Amount) + amount;

            if (totalPaid >= invoice.Amount)
            {
                invoice.Status = InvoiceStatus.Paid;
            }

            await _db.SaveChangesAsync(cancellationToken);

            try
            {
                await _generalLedgerService.PostPaymentReceiptAsync(
                    payment.Id,
                    _userManager.GetUserId(User),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "General ledger posting failed for manual payment {PaymentId} (invoice {InvoiceId}).",
                    payment.Id,
                    invoice.Id);
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        private static string GenerateInvoiceNumber()
        {
            // Simple unique-enough format for now; can be replaced with per-branch sequences later.
            return $"INV-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        }

        private IQueryable<Invoice> ApplyBranchScope(IQueryable<Invoice> query)
        {
            if (User.IsInRole("SuperAdmin"))
            {
                return query;
            }

            var branchId = User.GetBranchId();
            if (string.IsNullOrWhiteSpace(branchId))
            {
                return query.Where(_ => false);
            }

            return query.Where(invoice =>
                invoice.BranchId == branchId ||
                (invoice.BranchId == null && _db.UserClaims.Any(claim =>
                    claim.UserId == invoice.MemberUserId &&
                    claim.ClaimType == BranchAccess.BranchIdClaimType &&
                    claim.ClaimValue == branchId)));
        }

        private async Task PopulateMemberSelectListAsync(string? selectedMemberUserId = null)
        {
            var members = (await _userManager.GetUsersInRoleAsync("Member"))
                .OrderBy(user => user.Email)
                .ToList();

            if (!User.IsInRole("SuperAdmin"))
            {
                var branchId = User.GetBranchId();
                if (string.IsNullOrWhiteSpace(branchId))
                {
                    members.Clear();
                }
                else
                {
                    var memberIds = members.Select(user => user.Id).ToList();
                    var memberBranchById = await MemberBranchAssignment.ResolveHomeBranchMapAsync(_db, memberIds);
                    var scopedMemberIds = memberIds
                        .Where(memberId =>
                            memberBranchById.TryGetValue(memberId, out var memberBranchId) &&
                            string.Equals(memberBranchId, branchId, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    var scopedSet = scopedMemberIds.ToHashSet(StringComparer.Ordinal);
                    members = members
                        .Where(user => scopedSet.Contains(user.Id))
                        .OrderBy(user => user.Email)
                        .ToList();
                }
            }

            ViewBag.MemberUserId = new SelectList(members, "Id", "Email", selectedMemberUserId);
        }

        private async Task<bool> CanAccessMemberAsync(string? memberUserId)
        {
            if (string.IsNullOrWhiteSpace(memberUserId))
            {
                return false;
            }

            if (User.IsInRole("SuperAdmin"))
            {
                return true;
            }

            var branchId = User.GetBranchId();
            if (string.IsNullOrWhiteSpace(branchId))
            {
                return false;
            }

            var memberBranchId = await MemberBranchAssignment.ResolveHomeBranchIdAsync(_db, memberUserId);
            return string.Equals(memberBranchId, branchId, StringComparison.OrdinalIgnoreCase);
        }

        private async Task<string?> ResolveMemberBranchIdAsync(string? memberUserId)
        {
            return await MemberBranchAssignment.ResolveHomeBranchIdAsync(_db, memberUserId);
        }
    }
}
