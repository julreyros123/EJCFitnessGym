using System.Text.Json;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Admin;
using EJCFitnessGym.Models.Billing;
using EJCFitnessGym.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Controllers
{
    [ApiController]
    [Authorize(Roles = "Admin,SuperAdmin")]
    [Route("api/admin/dashboard")]
    public class AdminDashboardMetricsController : ControllerBase
    {
        private const int DefaultRangeDays = 30;
        private const int MaxRangeDays = 730;
        private const string MemberRoleName = "Member";
        private const string CheckInEventType = "staff.member.checkin";

        private readonly ApplicationDbContext _db;

        public AdminDashboardMetricsController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet("overview")]
        public async Task<IActionResult> GetOverview(
            [FromQuery] DateTime? fromUtc = null,
            [FromQuery] DateTime? toUtc = null,
            CancellationToken cancellationToken = default)
        {
            var (startUtc, endUtc) = NormalizeRange(fromUtc, toUtc);
            var endExclusiveUtc = endUtc.AddDays(1);
            var isSuperAdmin = User.IsInRole("SuperAdmin");
            var branchId = User.GetBranchId();

            if (!isSuperAdmin && string.IsNullOrWhiteSpace(branchId))
            {
                return Forbid();
            }

            var scopedMemberLookup = isSuperAdmin
                ? new HashSet<string>(StringComparer.Ordinal)
                : await GetScopedMemberUserIdsAsync(branchId, cancellationToken);

            var latestSubscriptions = await _db.MemberSubscriptions
                .AsNoTracking()
                .Where(subscription =>
                    isSuperAdmin || scopedMemberLookup.Contains(subscription.MemberUserId))
                .GroupBy(subscription => subscription.MemberUserId)
                .Select(group => group
                    .OrderByDescending(subscription => subscription.StartDateUtc)
                    .ThenByDescending(subscription => subscription.Id)
                    .Select(subscription => new LatestSubscriptionSnapshot
                    {
                        Status = subscription.Status,
                        EndDateUtc = subscription.EndDateUtc
                    })
                    .First())
                .ToListAsync(cancellationToken);

            var activeMembers = latestSubscriptions.Count(subscription =>
                subscription.Status == SubscriptionStatus.Active &&
                (!subscription.EndDateUtc.HasValue || subscription.EndDateUtc.Value.Date >= endUtc));

            var expiringWindowEndUtc = endUtc.AddDays(7);
            var expiringPlans = latestSubscriptions.Count(subscription =>
                subscription.Status == SubscriptionStatus.Active &&
                subscription.EndDateUtc.HasValue &&
                subscription.EndDateUtc.Value.Date >= endUtc &&
                subscription.EndDateUtc.Value.Date <= expiringWindowEndUtc);

            var followUpsQuery = _db.MemberRetentionActions
                .AsNoTracking()
                .Where(action =>
                    action.Status == MemberRetentionActionStatus.Open ||
                    action.Status == MemberRetentionActionStatus.InProgress);

            if (!isSuperAdmin)
            {
                followUpsQuery = followUpsQuery
                    .Where(action => scopedMemberLookup.Contains(action.MemberUserId));
            }

            var openFollowUps = await followUpsQuery.CountAsync(cancellationToken);

            var revenueRows = await (
                from payment in _db.Payments.AsNoTracking()
                join invoice in _db.Invoices.AsNoTracking() on payment.InvoiceId equals invoice.Id
                where payment.Status == PaymentStatus.Succeeded
                    && payment.PaidAtUtc >= startUtc
                    && payment.PaidAtUtc < endExclusiveUtc
                    && (isSuperAdmin || scopedMemberLookup.Contains(invoice.MemberUserId))
                group payment by payment.PaidAtUtc.Date
                into dayGroup
                select new DailyRevenueSnapshot
                {
                    DayUtc = dayGroup.Key,
                    Revenue = dayGroup.Sum(item => item.Amount)
                })
                .ToListAsync(cancellationToken);

            var checkInMessages = await _db.IntegrationOutboxMessages
                .AsNoTracking()
                .Where(message =>
                    message.EventType == CheckInEventType &&
                    message.CreatedUtc >= startUtc &&
                    message.CreatedUtc < endExclusiveUtc)
                .Select(message => new
                {
                    message.CreatedUtc,
                    message.PayloadJson
                })
                .ToListAsync(cancellationToken);

            var checkInsByDay = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var message in checkInMessages)
            {
                if (!IsCheckInInScope(
                        message.PayloadJson,
                        isSuperAdmin,
                        branchId,
                        scopedMemberLookup))
                {
                    continue;
                }

                var dayKey = message.CreatedUtc.Date.ToString("yyyy-MM-dd");
                checkInsByDay.TryGetValue(dayKey, out var currentCount);
                checkInsByDay[dayKey] = currentCount + 1;
            }

            var alertCountsByDay = await _db.FinanceAlertLogs
                .AsNoTracking()
                .Where(alert => alert.CreatedUtc >= startUtc && alert.CreatedUtc < endExclusiveUtc)
                .GroupBy(alert => alert.CreatedUtc.Date)
                .Select(group => group.Count())
                .ToListAsync(cancellationToken);

            var auditAlertsPeak = alertCountsByDay.Count == 0 ? 0 : alertCountsByDay.Max();

            var revenueByDay = revenueRows.ToDictionary(
                row => row.DayUtc.ToString("yyyy-MM-dd"),
                row => row.Revenue,
                StringComparer.Ordinal);

            var dayCount = (int)(endUtc - startUtc).TotalDays + 1;
            var totalRevenue = 0m;
            var totalCheckIns = 0;
            var dailyPoints = new List<DailyOverviewPoint>(dayCount);

            for (var day = startUtc.Date; day <= endUtc.Date; day = day.AddDays(1))
            {
                var dayKey = day.ToString("yyyy-MM-dd");
                revenueByDay.TryGetValue(dayKey, out var dayRevenue);
                checkInsByDay.TryGetValue(dayKey, out var dayCheckIns);

                totalRevenue += dayRevenue;
                totalCheckIns += dayCheckIns;

                dailyPoints.Add(new DailyOverviewPoint
                {
                    Date = dayKey,
                    Revenue = dayRevenue,
                    CheckIns = dayCheckIns
                });
            }

            var averageRevenuePerDay = dayCount <= 0
                ? 0m
                : decimal.Round(totalRevenue / dayCount, 2, MidpointRounding.AwayFromZero);

            return Ok(new AdminDashboardOverviewResponse
            {
                FromUtc = startUtc,
                ToUtc = endUtc,
                DayCount = dayCount,
                Kpis = new DashboardKpiResponse
                {
                    ActiveMembers = activeMembers,
                    CheckIns = totalCheckIns,
                    FollowUps = openFollowUps,
                    ExpiringPlans = expiringPlans,
                    AuditAlerts = auditAlertsPeak
                },
                Summary = new DashboardSummaryResponse
                {
                    TotalRevenue = totalRevenue,
                    AverageRevenuePerDay = averageRevenuePerDay
                },
                Daily = dailyPoints
            });
        }

        private async Task<HashSet<string>> GetScopedMemberUserIdsAsync(
            string? branchId,
            CancellationToken cancellationToken)
        {
            var memberRoleId = await _db.Roles
                .AsNoTracking()
                .Where(role => role.Name == MemberRoleName)
                .Select(role => role.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(memberRoleId))
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            if (string.IsNullOrWhiteSpace(branchId))
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var scopedIds = await (
                from userRole in _db.UserRoles.AsNoTracking()
                join claim in _db.UserClaims.AsNoTracking()
                    on userRole.UserId equals claim.UserId
                where userRole.RoleId == memberRoleId
                    && claim.ClaimType == BranchAccess.BranchIdClaimType
                    && claim.ClaimValue == branchId
                select userRole.UserId)
                .Distinct()
                .ToListAsync(cancellationToken);

            return scopedIds.ToHashSet(StringComparer.Ordinal);
        }

        private static bool IsCheckInInScope(
            string? payloadJson,
            bool isSuperAdmin,
            string? branchId,
            HashSet<string> scopedMemberLookup)
        {
            if (isSuperAdmin)
            {
                return true;
            }

            var payload = ParseAttendancePayload(payloadJson);
            var inBranchScope =
                !string.IsNullOrWhiteSpace(payload.BranchId) &&
                !string.IsNullOrWhiteSpace(branchId) &&
                string.Equals(payload.BranchId, branchId, StringComparison.OrdinalIgnoreCase);

            if (inBranchScope)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(payload.MemberUserId) &&
                scopedMemberLookup.Contains(payload.MemberUserId);
        }

        private static AttendancePayloadScope ParseAttendancePayload(string? payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                return new AttendancePayloadScope();
            }

            try
            {
                using var document = JsonDocument.Parse(payloadJson);
                var root = document.RootElement;
                return new AttendancePayloadScope
                {
                    MemberUserId = ReadPayloadString(root, "memberUserId"),
                    BranchId = ReadPayloadString(root, "branchId")
                };
            }
            catch (JsonException)
            {
                return new AttendancePayloadScope();
            }
        }

        private static string? ReadPayloadString(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var property))
            {
                return null;
            }

            var value = property.ValueKind switch
            {
                JsonValueKind.String => property.GetString(),
                JsonValueKind.Number => property.GetRawText(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                _ => null
            };

            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static (DateTime StartUtc, DateTime EndUtc) NormalizeRange(DateTime? fromUtc, DateTime? toUtc)
        {
            var todayUtc = DateTime.UtcNow.Date;
            var endUtc = NormalizeUtcDate(toUtc) ?? todayUtc;
            var startUtc = NormalizeUtcDate(fromUtc) ?? endUtc.AddDays(-(DefaultRangeDays - 1));

            if (endUtc > todayUtc)
            {
                endUtc = todayUtc;
            }

            if (startUtc > endUtc)
            {
                (startUtc, endUtc) = (endUtc, startUtc);
            }

            var maxSpanDays = MaxRangeDays - 1;
            if ((endUtc - startUtc).TotalDays > maxSpanDays)
            {
                startUtc = endUtc.AddDays(-maxSpanDays);
            }

            return (startUtc, endUtc);
        }

        private static DateTime? NormalizeUtcDate(DateTime? value)
        {
            if (!value.HasValue)
            {
                return null;
            }

            var utcValue = value.Value.Kind == DateTimeKind.Utc
                ? value.Value
                : value.Value.ToUniversalTime();

            return utcValue.Date;
        }

        private sealed class LatestSubscriptionSnapshot
        {
            public SubscriptionStatus Status { get; init; }
            public DateTime? EndDateUtc { get; init; }
        }

        private sealed class DailyRevenueSnapshot
        {
            public DateTime DayUtc { get; init; }
            public decimal Revenue { get; init; }
        }

        private sealed class AttendancePayloadScope
        {
            public string? MemberUserId { get; init; }
            public string? BranchId { get; init; }
        }

        private sealed class AdminDashboardOverviewResponse
        {
            public DateTime FromUtc { get; init; }
            public DateTime ToUtc { get; init; }
            public int DayCount { get; init; }
            public DashboardKpiResponse Kpis { get; init; } = new();
            public DashboardSummaryResponse Summary { get; init; } = new();
            public IReadOnlyList<DailyOverviewPoint> Daily { get; init; } = Array.Empty<DailyOverviewPoint>();
        }

        private sealed class DashboardKpiResponse
        {
            public int ActiveMembers { get; init; }
            public int CheckIns { get; init; }
            public int FollowUps { get; init; }
            public int ExpiringPlans { get; init; }
            public int AuditAlerts { get; init; }
        }

        private sealed class DashboardSummaryResponse
        {
            public decimal TotalRevenue { get; init; }
            public decimal AverageRevenuePerDay { get; init; }
        }

        private sealed class DailyOverviewPoint
        {
            public string Date { get; init; } = string.Empty;
            public decimal Revenue { get; init; }
            public int CheckIns { get; init; }
        }
    }
}
