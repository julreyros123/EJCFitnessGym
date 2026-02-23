using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Security;
using EJCFitnessGym.Services.Staff;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Pages.Staff
{
    public class AttendanceModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IStaffAttendanceService _staffAttendanceService;

        public AttendanceModel(ApplicationDbContext db, IStaffAttendanceService staffAttendanceService)
        {
            _db = db;
            _staffAttendanceService = staffAttendanceService;
        }

        public IReadOnlyList<AttendanceRow> Rows { get; private set; } = Array.Empty<AttendanceRow>();
        public int AutoCheckoutHours => (int)Math.Round(_staffAttendanceService.AutoCheckoutAfter.TotalHours);

        public int OnFloorCount { get; private set; }
        public int CompletedCount { get; private set; }
        public int PlanExpiringCount { get; private set; }

        public async Task OnGet(CancellationToken cancellationToken)
        {
            var todayUtc = DateTime.UtcNow.Date;
            var scopedBranchId = User.IsInRole("SuperAdmin") ? null : User.GetBranchId();
            await _staffAttendanceService.AutoCloseStaleSessionsAsync(scopedBranchId, cancellationToken);

            var attendanceEvents = await ReadAttendanceEventsAsync(todayUtc, cancellationToken);
            var groupedByMember = attendanceEvents
                .GroupBy(evt => evt.MemberUserId, StringComparer.Ordinal)
                .ToList();

            var latestSubscriptionByMember = await _db.MemberSubscriptions
                .AsNoTracking()
                .Where(subscription => groupedByMember.Select(group => group.Key).Contains(subscription.MemberUserId))
                .GroupBy(subscription => subscription.MemberUserId)
                .Select(group => group
                    .OrderByDescending(subscription => subscription.StartDateUtc)
                    .ThenByDescending(subscription => subscription.Id)
                    .First())
                .ToDictionaryAsync(subscription => subscription.MemberUserId, subscription => subscription, StringComparer.Ordinal, cancellationToken);

            var rows = new List<AttendanceRow>(groupedByMember.Count);
            foreach (var memberEvents in groupedByMember)
            {
                var orderedEvents = memberEvents
                    .OrderBy(evt => evt.EventUtc)
                    .ThenBy(evt => evt.EventId)
                    .ToList();
                if (orderedEvents.Count == 0)
                {
                    continue;
                }

                var firstCheckIn = orderedEvents
                    .FirstOrDefault(evt => StaffAttendanceEvents.IsCheckIn(evt.EventType));
                if (firstCheckIn is null)
                {
                    continue;
                }

                var latestCheckOut = orderedEvents
                    .Where(evt =>
                        StaffAttendanceEvents.IsCheckOut(evt.EventType) &&
                        evt.EventUtc >= firstCheckIn.EventUtc)
                    .OrderByDescending(evt => evt.EventUtc)
                    .ThenByDescending(evt => evt.EventId)
                    .FirstOrDefault();

                var hasAutoClosedByTimeout = latestCheckOut is null &&
                    _staffAttendanceService.IsSessionTimedOut(firstCheckIn.EventUtc);

                var effectiveEndUtc = latestCheckOut?.EventUtc ??
                    (hasAutoClosedByTimeout
                        ? firstCheckIn.EventUtc.Add(_staffAttendanceService.AutoCheckoutAfter)
                        : DateTime.UtcNow);
                var duration = effectiveEndUtc - firstCheckIn.EventUtc;
                if (duration < TimeSpan.Zero)
                {
                    duration = TimeSpan.Zero;
                }

                latestSubscriptionByMember.TryGetValue(memberEvents.Key, out var subscription);
                var membershipBadge = ResolveMembershipBadge(subscription, todayUtc);
                var onFloor = latestCheckOut is null && !hasAutoClosedByTimeout;
                var autoClosed = latestCheckOut?.IsAutoCheckout == true || hasAutoClosedByTimeout;

                rows.Add(new AttendanceRow
                {
                    MemberDisplayName = firstCheckIn.MemberDisplayName,
                    CheckInUtc = firstCheckIn.EventUtc,
                    CheckOutUtc = latestCheckOut?.EventUtc ?? (hasAutoClosedByTimeout ? effectiveEndUtc : null),
                    DurationLabel = FormatDuration(duration),
                    StatusLabel = onFloor ? "On Floor" : autoClosed ? "Auto Closed" : "Completed",
                    StatusBadgeClass = onFloor
                        ? "badge bg-info text-dark"
                        : autoClosed
                            ? "badge bg-warning text-dark"
                            : "badge ejc-badge",
                    MembershipStatusLabel = membershipBadge.Label,
                    MembershipStatusBadgeClass = membershipBadge.BadgeClass
                });
            }

            Rows = rows
                .OrderByDescending(row => row.CheckInUtc)
                .ThenBy(row => row.MemberDisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            OnFloorCount = Rows.Count(row => string.Equals(row.StatusLabel, "On Floor", StringComparison.OrdinalIgnoreCase));
            CompletedCount = Rows.Count(row =>
                string.Equals(row.StatusLabel, "Completed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(row.StatusLabel, "Auto Closed", StringComparison.OrdinalIgnoreCase));
            PlanExpiringCount = Rows.Count(row => string.Equals(row.MembershipStatusLabel, "Plan Expiring", StringComparison.OrdinalIgnoreCase));
        }

        private async Task<List<StaffAttendanceEvent>> ReadAttendanceEventsAsync(DateTime minUtc, CancellationToken cancellationToken)
        {
            var messages = await _db.IntegrationOutboxMessages
                .AsNoTracking()
                .Where(message =>
                    (message.EventType == StaffAttendanceEvents.CheckInEventType ||
                     message.EventType == StaffAttendanceEvents.CheckOutEventType) &&
                    message.CreatedUtc >= minUtc)
                .OrderByDescending(message => message.CreatedUtc)
                .ThenByDescending(message => message.Id)
                .Take(800)
                .ToListAsync(cancellationToken);

            return messages
                .Select(StaffAttendanceEvents.TryParse)
                .Where(evt => evt is not null && IsInCurrentBranchScope(evt.BranchId))
                .Cast<StaffAttendanceEvent>()
                .ToList();
        }

        private bool IsInCurrentBranchScope(string? branchId)
        {
            if (User.IsInRole("SuperAdmin"))
            {
                return true;
            }

            var currentBranchId = User.GetBranchId();
            return !string.IsNullOrWhiteSpace(currentBranchId) &&
                !string.IsNullOrWhiteSpace(branchId) &&
                string.Equals(currentBranchId, branchId, StringComparison.OrdinalIgnoreCase);
        }

        private static (string Label, string BadgeClass) ResolveMembershipBadge(MemberSubscription? subscription, DateTime todayUtc)
        {
            if (subscription is null)
            {
                return ("No Plan", "badge bg-secondary");
            }

            if (subscription.Status != SubscriptionStatus.Active)
            {
                return (subscription.Status.ToString(), "badge bg-secondary");
            }

            if (!subscription.EndDateUtc.HasValue)
            {
                return ("Active", "badge ejc-badge");
            }

            var endDateUtc = subscription.EndDateUtc.Value.Date;
            if (endDateUtc < todayUtc)
            {
                return ("Expired", "badge bg-danger");
            }

            if (endDateUtc <= todayUtc.AddDays(3))
            {
                return ("Plan Expiring", "badge bg-warning text-dark");
            }

            return ("Active", "badge ejc-badge");
        }

        private static string FormatDuration(TimeSpan value)
        {
            var hours = (int)value.TotalHours;
            var minutes = value.Minutes;
            return $"{hours}h {minutes:D2}m";
        }

        public sealed class AttendanceRow
        {
            public string MemberDisplayName { get; init; } = string.Empty;
            public DateTime CheckInUtc { get; init; }
            public DateTime? CheckOutUtc { get; init; }
            public string DurationLabel { get; init; } = "0h 00m";
            public string StatusLabel { get; init; } = string.Empty;
            public string StatusBadgeClass { get; init; } = string.Empty;
            public string MembershipStatusLabel { get; init; } = string.Empty;
            public string MembershipStatusBadgeClass { get; init; } = "badge bg-secondary";
        }
    }
}
