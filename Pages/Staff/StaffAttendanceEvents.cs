using System.Text.Json;
using EJCFitnessGym.Models.Integration;

namespace EJCFitnessGym.Pages.Staff
{
    public static class StaffAttendanceEvents
    {
        public const string CheckInEventType = "staff.member.checkin";
        public const string CheckOutEventType = "staff.member.checkout";

        public static bool IsAttendanceEvent(string eventType) =>
            string.Equals(eventType, CheckInEventType, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(eventType, CheckOutEventType, StringComparison.OrdinalIgnoreCase);

        public static bool IsCheckIn(string eventType) =>
            string.Equals(eventType, CheckInEventType, StringComparison.OrdinalIgnoreCase);

        public static bool IsCheckOut(string eventType) =>
            string.Equals(eventType, CheckOutEventType, StringComparison.OrdinalIgnoreCase);

        public static StaffAttendanceEvent? TryParse(IntegrationOutboxMessage message)
        {
            if (!IsAttendanceEvent(message.EventType))
            {
                return null;
            }

            var payloadMemberUserId = TryGetPayloadString(message.PayloadJson, "memberUserId");
            if (string.IsNullOrWhiteSpace(payloadMemberUserId))
            {
                return null;
            }

            return new StaffAttendanceEvent
            {
                EventId = message.Id,
                MemberUserId = payloadMemberUserId.Trim(),
                MemberDisplayName = TryGetPayloadString(message.PayloadJson, "memberDisplayName") ?? payloadMemberUserId.Trim(),
                BranchId = TryGetPayloadString(message.PayloadJson, "branchId"),
                HandledByUserId = TryGetPayloadString(message.PayloadJson, "handledByUserId"),
                IsAutoCheckout = TryGetPayloadBool(message.PayloadJson, "isAutoCheckout"),
                EventType = message.EventType,
                EventUtc = message.CreatedUtc
            };
        }

        public static string ActionLabel(string eventType, bool isAutoCheckout = false)
        {
            if (IsCheckIn(eventType))
            {
                return "Check In";
            }

            if (IsCheckOut(eventType))
            {
                return isAutoCheckout ? "Auto Check Out" : "Check Out";
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
    }

    public sealed class StaffAttendanceEvent
    {
        public int EventId { get; init; }
        public string MemberUserId { get; init; } = string.Empty;
        public string MemberDisplayName { get; init; } = string.Empty;
        public string? BranchId { get; init; }
        public string? HandledByUserId { get; init; }
        public bool IsAutoCheckout { get; init; }
        public string EventType { get; init; } = string.Empty;
        public DateTime EventUtc { get; init; }
    }
}
