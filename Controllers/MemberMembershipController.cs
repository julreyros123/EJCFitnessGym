using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Services.Memberships;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Controllers
{
    [ApiController]
    [Authorize(Roles = "Member")]
    [Route("api/member/membership")]
    public class MemberMembershipController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IMembershipService _membershipService;

        public MemberMembershipController(
            ApplicationDbContext db,
            UserManager<IdentityUser> userManager,
            IMembershipService membershipService)
        {
            _db = db;
            _userManager = userManager;
            _membershipService = membershipService;
        }

        [HttpGet]
        public async Task<IActionResult> GetCurrent(CancellationToken cancellationToken)
        {
            var memberUserId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(memberUserId))
            {
                return Unauthorized();
            }

            await _membershipService.RunLifecycleMaintenanceAsync(cancellationToken: cancellationToken);

            var subscription = await _membershipService.GetLatestSubscriptionAsync(memberUserId, cancellationToken);

            var nextPaymentDueDateUtc = await _db.Invoices
                .AsNoTracking()
                .Where(i => i.MemberUserId == memberUserId && (i.Status == InvoiceStatus.Unpaid || i.Status == InvoiceStatus.Overdue))
                .OrderBy(i => i.DueDateUtc)
                .Select(i => (DateTime?)i.DueDateUtc)
                .FirstOrDefaultAsync(cancellationToken);

            var outstandingBalance = await _db.Invoices
                .AsNoTracking()
                .Where(i => i.MemberUserId == memberUserId && (i.Status == InvoiceStatus.Unpaid || i.Status == InvoiceStatus.Overdue))
                .SumAsync(i => (decimal?)i.Amount, cancellationToken) ?? 0m;

            var totalPaid = await _db.Invoices
                .AsNoTracking()
                .Where(i => i.MemberUserId == memberUserId && i.Status == InvoiceStatus.Paid)
                .SumAsync(i => (decimal?)i.Amount, cancellationToken) ?? 0m;

            return Ok(new
            {
                hasSubscription = subscription is not null,
                planName = subscription?.SubscriptionPlan?.Name,
                status = subscription?.Status.ToString(),
                startDateUtc = subscription?.StartDateUtc,
                endDateUtc = subscription?.EndDateUtc,
                nextPaymentDueDateUtc,
                outstandingBalance,
                totalPaid
            });
        }

        [HttpGet("history")]
        public async Task<IActionResult> GetHistory([FromQuery] int take = 12, CancellationToken cancellationToken = default)
        {
            var memberUserId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(memberUserId))
            {
                return Unauthorized();
            }

            await _membershipService.RunLifecycleMaintenanceAsync(cancellationToken: cancellationToken);

            var subscriptions = await _membershipService.GetSubscriptionHistoryAsync(memberUserId, take, cancellationToken);

            return Ok(subscriptions.Select(s => new
            {
                id = s.Id,
                planId = s.SubscriptionPlanId,
                planName = s.SubscriptionPlan?.Name,
                status = s.Status.ToString(),
                startDateUtc = s.StartDateUtc,
                endDateUtc = s.EndDateUtc,
                externalSubscriptionId = s.ExternalSubscriptionId
            }));
        }
    }
}
