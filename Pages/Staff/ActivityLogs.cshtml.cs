using System.Text.Json;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Integration;
using EJCFitnessGym.Security;
using EJCFitnessGym.Services.Staff;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace EJCFitnessGym.Pages.Staff
{
    public class ActivityLogsModel : PageModel
    {
        private readonly ApplicationDbContext _db;
        private readonly IStaffAttendanceService _staffAttendanceService;

        public ActivityLogsModel(ApplicationDbContext db, IStaffAttendanceService staffAttendanceService)
        {
            _db = db;
            _staffAttendanceService = staffAttendanceService;
        }

        public IReadOnlyList<ActivityLogRow> Rows { get; private set; } = Array.Empty<ActivityLogRow>();

        public async Task OnGet(CancellationToken cancellationToken)
        {
            var scopedBranchId = User.IsInRole("SuperAdmin") ? null : User.GetBranchId();
            await _staffAttendanceService.AutoCloseStaleSessionsAsync(scopedBranchId, cancellationToken);

            var candidateMessages = await _db.IntegrationOutboxMessages
                .AsNoTracking()
                .Where(message =>
                    message.EventType == StaffAttendanceEvents.CheckInEventType ||
                    message.EventType == StaffAttendanceEvents.CheckOutEventType ||
                    message.EventType == "payment.checkout.created" ||
                    message.EventType == "payment.failed" ||
                    message.EventType == "payment.succeeded" ||
                    message.EventType == "billing.invoice.reminder")
                .OrderByDescending(message => message.CreatedUtc)
                .ThenByDescending(message => message.Id)
                .Take(200)
                .ToListAsync(cancellationToken);

            var rows = candidateMessages
                .Where(message => IsInCurrentBranchScope(TryGetPayloadString(message.PayloadJson, "branchId")))
                .Select(message =>
                {
                    var memberDisplayName = TryGetPayloadString(message.PayloadJson, "memberDisplayName");
                    var memberUserId = TryGetPayloadString(message.PayloadJson, "memberUserId");
                    var actorUserId = TryGetPayloadString(message.PayloadJson, "handledByUserId");
                    var isAutoCheckout = TryGetPayloadBool(message.PayloadJson, "isAutoCheckout");

                    return new ActivityLogRow
                    {
                        TimeLocal = message.CreatedUtc.ToLocalTime(),
                        EventTypeLabel = ToEventLabel(message.EventType, isAutoCheckout),
                        HandledBy = string.IsNullOrWhiteSpace(actorUserId) ? "-" : actorUserId!,
                        Member = !string.IsNullOrWhiteSpace(memberDisplayName)
                            ? memberDisplayName!
                            : !string.IsNullOrWhiteSpace(memberUserId)
                                ? memberUserId!
                                : "-",
                        Result = message.Message,
                        DeliveryState = message.Status.ToString()
                    };
                })
                .ToList();

            Rows = rows;
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

        private static string ToEventLabel(string eventType, bool isAutoCheckout)
        {
            if (string.Equals(eventType, StaffAttendanceEvents.CheckInEventType, StringComparison.OrdinalIgnoreCase))
            {
                return "Member Check-In";
            }

            if (string.Equals(eventType, StaffAttendanceEvents.CheckOutEventType, StringComparison.OrdinalIgnoreCase))
            {
                return isAutoCheckout ? "Member Auto Check-Out" : "Member Check-Out";
            }

            if (string.Equals(eventType, "payment.checkout.created", StringComparison.OrdinalIgnoreCase))
            {
                return "Checkout Started";
            }

            if (string.Equals(eventType, "payment.failed", StringComparison.OrdinalIgnoreCase))
            {
                return "Payment Failed";
            }

            if (string.Equals(eventType, "payment.succeeded", StringComparison.OrdinalIgnoreCase))
            {
                return "Payment Succeeded";
            }

            if (string.Equals(eventType, "billing.invoice.reminder", StringComparison.OrdinalIgnoreCase))
            {
                return "Billing Reminder";
            }

            return eventType;
        }

        private static string? TryGetPayloadString(string? payloadJson, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(payloadJson);
                if (!document.RootElement.TryGetProperty(propertyName, out var property))
                {
                    return null;
                }

                return property.ValueKind switch
                {
                    JsonValueKind.String => property.GetString(),
                    JsonValueKind.Number => property.GetRawText(),
                    JsonValueKind.True => bool.TrueString,
                    JsonValueKind.False => bool.FalseString,
                    _ => null
                };
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static bool TryGetPayloadBool(string? payloadJson, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(payloadJson);
                if (!document.RootElement.TryGetProperty(propertyName, out var property))
                {
                    return false;
                }

                return property.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.String => bool.TryParse(property.GetString(), out var parsed) && parsed,
                    _ => false
                };
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public sealed class ActivityLogRow
        {
            public DateTime TimeLocal { get; init; }
            public string EventTypeLabel { get; init; } = string.Empty;
            public string HandledBy { get; init; } = string.Empty;
            public string Member { get; init; } = string.Empty;
            public string Result { get; init; } = string.Empty;
            public string DeliveryState { get; init; } = string.Empty;
        }
    }
}
