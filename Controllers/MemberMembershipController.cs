using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Services.Memberships;
using EJCFitnessGym.Services.Payments;
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
        private readonly IPayMongoMembershipReconciliationService? _payMongoMembershipReconciliationService;

        public MemberMembershipController(
            ApplicationDbContext db,
            UserManager<IdentityUser> userManager,
            IMembershipService membershipService,
            IPayMongoMembershipReconciliationService? payMongoMembershipReconciliationService = null)
        {
            _db = db;
            _userManager = userManager;
            _membershipService = membershipService;
            _payMongoMembershipReconciliationService = payMongoMembershipReconciliationService;
        }

        [HttpGet]
        public async Task<IActionResult> GetCurrent(CancellationToken cancellationToken)
        {
            var memberUserId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(memberUserId))
            {
                return Unauthorized();
            }

            if (_payMongoMembershipReconciliationService is not null)
            {
                try
                {
                    await _payMongoMembershipReconciliationService
                        .ReconcilePendingMemberPaymentsAsync(memberUserId, cancellationToken);
                }
                catch
                {
                    // Keep membership endpoints available even if reconciliation is temporarily failing.
                }
            }

            await _membershipService.RunLifecycleMaintenanceAsync(cancellationToken: cancellationToken);

            var subscription = await _membershipService.GetLatestSubscriptionAsync(memberUserId, cancellationToken);

            var nextPaymentDueDateUtc = await _db.Invoices
                .AsNoTracking()
                .Where(i => i.MemberUserId == memberUserId && (i.Status == InvoiceStatus.Unpaid || i.Status == InvoiceStatus.Overdue))
                .OrderBy(i => i.DueDateUtc)
                .Select(i => (DateTime?)i.DueDateUtc)
                .FirstOrDefaultAsync(cancellationToken);

            nextPaymentDueDateUtc ??= subscription?.EndDateUtc;

            var nowUtc = DateTime.UtcNow;
            var outstandingBalance = await _db.Invoices
                .AsNoTracking()
                .Where(i =>
                    i.MemberUserId == memberUserId &&
                    (i.Status == InvoiceStatus.Overdue ||
                     (i.Status == InvoiceStatus.Unpaid && i.DueDateUtc <= nowUtc)))
                .SumAsync(i => (decimal?)i.Amount, cancellationToken) ?? 0m;

            var scheduledBalance = await _db.Invoices
                .AsNoTracking()
                .Where(i =>
                    i.MemberUserId == memberUserId &&
                    i.Status == InvoiceStatus.Unpaid &&
                    i.DueDateUtc > nowUtc)
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
                scheduledBalance,
                totalPaid
            });
        }

        [HttpGet("plans")]
        public async Task<IActionResult> GetPlans(CancellationToken cancellationToken)
        {
            var memberUserId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(memberUserId))
            {
                return Unauthorized();
            }

            if (_payMongoMembershipReconciliationService is not null)
            {
                try
                {
                    await _payMongoMembershipReconciliationService
                        .ReconcilePendingMemberPaymentsAsync(memberUserId, cancellationToken);
                }
                catch
                {
                    // Keep plan listing available even if reconciliation is temporarily failing.
                }
            }

            await _membershipService.RunLifecycleMaintenanceAsync(cancellationToken: cancellationToken);

            var currentSubscription = await _membershipService.GetLatestSubscriptionAsync(memberUserId, cancellationToken);
            var currentPlanId = currentSubscription?.SubscriptionPlanId;

            var plans = await _db.SubscriptionPlans
                .AsNoTracking()
                .Where(p => p.IsActive)
                .OrderBy(p => p.Price)
                .ThenBy(p => p.Name)
                .Select(p => new
                {
                    id = p.Id,
                    name = p.Name,
                    description = p.Description,
                    price = p.Price,
                    billingCycle = p.BillingCycle.ToString(),
                    isCurrentPlan = currentPlanId.HasValue && p.Id == currentPlanId.Value
                })
                .ToListAsync(cancellationToken);

            return Ok(new
            {
                currentPlanId,
                currentPlanName = currentSubscription?.SubscriptionPlan?.Name,
                hasActiveMembership = currentSubscription is not null && currentSubscription.Status == SubscriptionStatus.Active,
                plans
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

            if (_payMongoMembershipReconciliationService is not null)
            {
                try
                {
                    await _payMongoMembershipReconciliationService
                        .ReconcilePendingMemberPaymentsAsync(memberUserId, cancellationToken);
                }
                catch
                {
                    // Keep history endpoint available even if reconciliation is temporarily failing.
                }
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
