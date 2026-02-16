using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Services.Integration;
using EJCFitnessGym.Services.Memberships;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Controllers
{
    [ApiController]
    [Authorize(Roles = "Admin,Finance,SuperAdmin")]
    [Route("api/admin/memberships")]
    public class AdminMembershipController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IMembershipService _membershipService;
        private readonly IIntegrationOutbox _outbox;

        public AdminMembershipController(
            ApplicationDbContext db,
            IMembershipService membershipService,
            IIntegrationOutbox outbox)
        {
            _db = db;
            _membershipService = membershipService;
            _outbox = outbox;
        }

        [HttpGet("{memberUserId}/current")]
        public async Task<IActionResult> GetCurrent(string memberUserId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(memberUserId))
            {
                return BadRequest("memberUserId is required.");
            }

            await _membershipService.RunLifecycleMaintenanceAsync(cancellationToken: cancellationToken);

            var subscription = await _membershipService.GetLatestSubscriptionAsync(memberUserId, cancellationToken);
            if (subscription is null)
            {
                return NotFound(new { message = "No subscription record found for this member." });
            }

            return Ok(new
            {
                id = subscription.Id,
                memberUserId = subscription.MemberUserId,
                planId = subscription.SubscriptionPlanId,
                planName = subscription.SubscriptionPlan?.Name,
                status = subscription.Status.ToString(),
                startDateUtc = subscription.StartDateUtc,
                endDateUtc = subscription.EndDateUtc,
                externalSubscriptionId = subscription.ExternalSubscriptionId
            });
        }

        [HttpPost("{memberUserId}/renew")]
        [Authorize(Roles = "Finance,SuperAdmin")]
        public async Task<IActionResult> Renew(string memberUserId, [FromBody] RenewMembershipRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(memberUserId))
            {
                return BadRequest("memberUserId is required.");
            }

            await _membershipService.RunLifecycleMaintenanceAsync(cancellationToken: cancellationToken);

            var latest = await _membershipService.GetLatestSubscriptionAsync(memberUserId, cancellationToken);
            var targetPlanId = request.PlanId ?? latest?.SubscriptionPlanId;
            if (!targetPlanId.HasValue || targetPlanId.Value <= 0)
            {
                return BadRequest("A planId is required when no previous subscription exists.");
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            var renewed = await _membershipService.ActivateSubscriptionAsync(
                memberUserId,
                targetPlanId.Value,
                request.StartDateUtc,
                request.ExternalSubscriptionId,
                request.ExternalCustomerId,
                cancellationToken);

            var planName = await _db.SubscriptionPlans
                .Where(p => p.Id == renewed.SubscriptionPlanId)
                .Select(p => p.Name)
                .FirstOrDefaultAsync(cancellationToken);

            var renewPayload = new
            {
                subscriptionId = renewed.Id,
                memberUserId = renewed.MemberUserId,
                planId = renewed.SubscriptionPlanId,
                planName,
                status = renewed.Status.ToString(),
                startDateUtc = renewed.StartDateUtc,
                endDateUtc = renewed.EndDateUtc,
                externalSubscriptionId = renewed.ExternalSubscriptionId
            };

            await _outbox.EnqueueUserAsync(
                renewed.MemberUserId,
                "membership.renewed",
                "Your membership has been renewed.",
                renewPayload,
                cancellationToken);

            await _outbox.EnqueueBackOfficeAsync(
                "membership.renewed",
                "A membership renewal was processed.",
                renewPayload,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Ok(new
            {
                id = renewed.Id,
                memberUserId = renewed.MemberUserId,
                planId = renewed.SubscriptionPlanId,
                status = renewed.Status.ToString(),
                startDateUtc = renewed.StartDateUtc,
                endDateUtc = renewed.EndDateUtc,
                externalSubscriptionId = renewed.ExternalSubscriptionId
            });
        }

        [HttpPost("{memberUserId}/pause")]
        [Authorize(Roles = "Finance,SuperAdmin")]
        public async Task<IActionResult> Pause(string memberUserId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(memberUserId))
            {
                return BadRequest("memberUserId is required.");
            }

            await _membershipService.RunLifecycleMaintenanceAsync(cancellationToken: cancellationToken);

            var subscription = await _membershipService.GetLatestSubscriptionAsync(memberUserId, cancellationToken);
            if (subscription is null)
            {
                return NotFound(new { message = "No subscription to pause." });
            }

            if (subscription.Status == SubscriptionStatus.Cancelled || subscription.Status == SubscriptionStatus.Expired)
            {
                return BadRequest(new { message = $"Cannot pause a {subscription.Status} subscription." });
            }

            if (subscription.Status == SubscriptionStatus.Paused)
            {
                return Ok(new { message = "Membership is already paused.", subscriptionId = subscription.Id });
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            var tracked = await UpdateStatusAsync(subscription.Id, SubscriptionStatus.Paused, null, cancellationToken);
            if (tracked is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return NotFound(new { message = "Subscription was not found while processing pause." });
            }

            var pausePayload = new
            {
                subscriptionId = subscription.Id,
                memberUserId = subscription.MemberUserId,
                planId = subscription.SubscriptionPlanId,
                planName = subscription.SubscriptionPlan?.Name,
                status = SubscriptionStatus.Paused.ToString()
            };

            await _outbox.EnqueueUserAsync(
                subscription.MemberUserId,
                "membership.paused",
                "Your membership has been paused.",
                pausePayload,
                cancellationToken);

            await _outbox.EnqueueBackOfficeAsync(
                "membership.paused",
                "A membership was paused.",
                pausePayload,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Ok(new { message = "Membership paused.", subscriptionId = subscription.Id });
        }

        [HttpPost("{memberUserId}/resume")]
        [Authorize(Roles = "Finance,SuperAdmin")]
        public async Task<IActionResult> Resume(string memberUserId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(memberUserId))
            {
                return BadRequest("memberUserId is required.");
            }

            await _membershipService.RunLifecycleMaintenanceAsync(cancellationToken: cancellationToken);

            var resumed = await _membershipService.ResumeSubscriptionAsync(memberUserId, cancellationToken: cancellationToken);
            if (resumed is null)
            {
                return NotFound(new { message = "No resumable subscription found for this member." });
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            var resumePayload = new
            {
                subscriptionId = resumed.Id,
                memberUserId = resumed.MemberUserId,
                planId = resumed.SubscriptionPlanId,
                planName = resumed.SubscriptionPlan?.Name,
                status = resumed.Status.ToString(),
                startDateUtc = resumed.StartDateUtc,
                endDateUtc = resumed.EndDateUtc
            };

            await _outbox.EnqueueUserAsync(
                resumed.MemberUserId,
                "membership.resumed",
                "Your membership has been resumed.",
                resumePayload,
                cancellationToken);

            await _outbox.EnqueueBackOfficeAsync(
                "membership.resumed",
                "A membership was resumed.",
                resumePayload,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Ok(new
            {
                message = "Membership resumed.",
                subscriptionId = resumed.Id,
                status = resumed.Status.ToString(),
                startDateUtc = resumed.StartDateUtc,
                endDateUtc = resumed.EndDateUtc
            });
        }

        [HttpPost("{memberUserId}/cancel")]
        [Authorize(Roles = "Finance,SuperAdmin")]
        public async Task<IActionResult> Cancel(string memberUserId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(memberUserId))
            {
                return BadRequest("memberUserId is required.");
            }

            await _membershipService.RunLifecycleMaintenanceAsync(cancellationToken: cancellationToken);

            var subscription = await _membershipService.GetLatestSubscriptionAsync(memberUserId, cancellationToken);
            if (subscription is null)
            {
                return NotFound(new { message = "No subscription to cancel." });
            }

            if (subscription.Status == SubscriptionStatus.Cancelled)
            {
                return Ok(new { message = "Membership is already cancelled.", subscriptionId = subscription.Id });
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            var tracked = await UpdateStatusAsync(subscription.Id, SubscriptionStatus.Cancelled, DateTime.UtcNow, cancellationToken);
            if (tracked is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return NotFound(new { message = "Subscription was not found while processing cancellation." });
            }

            var cancelPayload = new
            {
                subscriptionId = subscription.Id,
                memberUserId = subscription.MemberUserId,
                planId = subscription.SubscriptionPlanId,
                planName = subscription.SubscriptionPlan?.Name,
                status = SubscriptionStatus.Cancelled.ToString()
            };

            await _outbox.EnqueueUserAsync(
                subscription.MemberUserId,
                "membership.cancelled",
                "Your membership has been cancelled.",
                cancelPayload,
                cancellationToken);

            await _outbox.EnqueueBackOfficeAsync(
                "membership.cancelled",
                "A membership was cancelled.",
                cancelPayload,
                cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Ok(new { message = "Membership cancelled.", subscriptionId = subscription.Id });
        }

        [HttpPost("lifecycle/run")]
        [Authorize(Roles = "Finance,SuperAdmin")]
        public async Task<IActionResult> RunLifecycleMaintenance(CancellationToken cancellationToken)
        {
            var result = await _membershipService.RunLifecycleMaintenanceAsync(cancellationToken: cancellationToken);
            return Ok(new
            {
                asOfUtc = result.AsOfUtc,
                expiredSubscriptions = result.ExpiredSubscriptions,
                overdueInvoices = result.OverdueInvoices
            });
        }

        private async Task<MemberSubscription?> UpdateStatusAsync(int subscriptionId, SubscriptionStatus status, DateTime? endDateUtc, CancellationToken cancellationToken)
        {
            var tracked = await _db.MemberSubscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, cancellationToken);
            if (tracked is null)
            {
                return null;
            }

            tracked.Status = status;
            if (endDateUtc.HasValue)
            {
                tracked.EndDateUtc = endDateUtc.Value;
            }

            return tracked;
        }

        public sealed class RenewMembershipRequest
        {
            public int? PlanId { get; set; }
            public DateTime? StartDateUtc { get; set; }
            public string? ExternalSubscriptionId { get; set; }
            public string? ExternalCustomerId { get; set; }
        }
    }
}
