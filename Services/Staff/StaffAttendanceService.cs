using EJCFitnessGym.Data;
using EJCFitnessGym.Pages.Staff;
using EJCFitnessGym.Services.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EJCFitnessGym.Services.Staff
{
    public sealed class StaffAttendanceService : IStaffAttendanceService
    {
        private readonly ApplicationDbContext _db;
        private readonly IIntegrationOutbox _integrationOutbox;
        private readonly IOptionsMonitor<StaffAttendanceOptions> _optionsMonitor;

        public StaffAttendanceService(
            ApplicationDbContext db,
            IIntegrationOutbox integrationOutbox,
            IOptionsMonitor<StaffAttendanceOptions> optionsMonitor)
        {
            _db = db;
            _integrationOutbox = integrationOutbox;
            _optionsMonitor = optionsMonitor;
        }

        public TimeSpan AutoCheckoutAfter =>
            TimeSpan.FromHours(Math.Clamp(_optionsMonitor.CurrentValue.AutoCheckoutHours, 1, 24));

        public bool IsSessionTimedOut(DateTime checkInUtc, DateTime? asOfUtc = null)
        {
            if (!_optionsMonitor.CurrentValue.AutoCheckoutEnabled)
            {
                return false;
            }

            var effectiveAsOfUtc = asOfUtc ?? DateTime.UtcNow;
            return checkInUtc.Add(AutoCheckoutAfter) <= effectiveAsOfUtc;
        }

        public async Task<int> AutoCloseStaleSessionsAsync(
            string? branchId = null,
            CancellationToken cancellationToken = default)
        {
            var options = _optionsMonitor.CurrentValue;
            if (!options.AutoCheckoutEnabled)
            {
                return 0;
            }

            var normalizedBranchId = NormalizeBranchId(branchId);
            var nowUtc = DateTime.UtcNow;
            var lookbackDays = Math.Clamp(options.LookbackDays, 1, 30);
            var maxEvents = Math.Clamp(options.MaxEventsPerSweep, 200, 10000);
            var windowStartUtc = nowUtc.AddDays(-lookbackDays);

            var candidateMessages = await _db.IntegrationOutboxMessages
                .AsNoTracking()
                .Where(message =>
                    (message.EventType == StaffAttendanceEvents.CheckInEventType ||
                     message.EventType == StaffAttendanceEvents.CheckOutEventType) &&
                    message.CreatedUtc >= windowStartUtc)
                .OrderByDescending(message => message.CreatedUtc)
                .ThenByDescending(message => message.Id)
                .Take(maxEvents)
                .ToListAsync(cancellationToken);

            var attendanceEvents = candidateMessages
                .Select(StaffAttendanceEvents.TryParse)
                .Where(evt => evt is not null)
                .Cast<StaffAttendanceEvent>();

            if (!string.IsNullOrWhiteSpace(normalizedBranchId))
            {
                attendanceEvents = attendanceEvents
                    .Where(evt =>
                        !string.IsNullOrWhiteSpace(evt.BranchId) &&
                        string.Equals(evt.BranchId, normalizedBranchId, StringComparison.OrdinalIgnoreCase));
            }

            var latestByMember = attendanceEvents
                .GroupBy(evt => evt.MemberUserId, StringComparer.Ordinal)
                .Select(group => group
                    .OrderByDescending(evt => evt.EventUtc)
                    .ThenByDescending(evt => evt.EventId)
                    .First())
                .ToList();

            if (latestByMember.Count == 0)
            {
                return 0;
            }

            var autoClosedCount = 0;
            foreach (var latestEvent in latestByMember)
            {
                if (!StaffAttendanceEvents.IsCheckIn(latestEvent.EventType))
                {
                    continue;
                }

                if (!IsSessionTimedOut(latestEvent.EventUtc, nowUtc))
                {
                    continue;
                }

                var autoCheckoutAtUtc = latestEvent.EventUtc.Add(AutoCheckoutAfter);
                var actorUserId = "system:auto-checkout";
                var payload = new
                {
                    memberUserId = latestEvent.MemberUserId,
                    memberDisplayName = latestEvent.MemberDisplayName,
                    branchId = latestEvent.BranchId,
                    handledByUserId = actorUserId,
                    handledBy = "System",
                    actionAtUtc = autoCheckoutAtUtc,
                    sourceCheckInEventId = latestEvent.EventId,
                    isAutoCheckout = true,
                    autoCheckoutHours = (int)AutoCheckoutAfter.TotalHours,
                    reason = "No manual checkout detected within attendance timeout."
                };

                await _integrationOutbox.EnqueueBackOfficeAsync(
                    StaffAttendanceEvents.CheckOutEventType,
                    $"Session auto-checked-out for {latestEvent.MemberDisplayName} after {(int)AutoCheckoutAfter.TotalHours} hours.",
                    payload,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(latestEvent.MemberUserId))
                {
                    await _integrationOutbox.EnqueueUserAsync(
                        latestEvent.MemberUserId,
                        StaffAttendanceEvents.CheckOutEventType,
                        $"Your session was auto-checked-out after {(int)AutoCheckoutAfter.TotalHours} hours.",
                        payload,
                        cancellationToken);
                }

                autoClosedCount++;
            }

            if (autoClosedCount > 0)
            {
                await _db.SaveChangesAsync(cancellationToken);
            }

            return autoClosedCount;
        }

        private static string? NormalizeBranchId(string? branchId)
        {
            if (string.IsNullOrWhiteSpace(branchId))
            {
                return null;
            }

            return branchId.Trim();
        }
    }
}
