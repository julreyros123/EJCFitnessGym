using EJCFitnessGym.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace EJCFitnessGym.Services.Realtime
{
    public class SignalRErpEventPublisher : IErpEventPublisher
    {
        private readonly IHubContext<ErpEventsHub> _hubContext;
        private readonly ILogger<SignalRErpEventPublisher> _logger;

        public SignalRErpEventPublisher(
            IHubContext<ErpEventsHub> hubContext,
            ILogger<SignalRErpEventPublisher> logger)
        {
            _hubContext = hubContext;
            _logger = logger;
        }

        public Task PublishToBackOfficeAsync(
            string eventType,
            string message,
            object? data = null,
            CancellationToken cancellationToken = default)
            => PublishToGroupAsync("role:BackOffice", eventType, message, data, cancellationToken);

        public Task PublishToRoleAsync(
            string role,
            string eventType,
            string message,
            object? data = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                return Task.CompletedTask;
            }

            return PublishToGroupAsync($"role:{role.Trim()}", eventType, message, data, cancellationToken);
        }

        public Task PublishToUserAsync(
            string userId,
            string eventType,
            string message,
            object? data = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Task.CompletedTask;
            }

            return PublishToGroupAsync($"user:{userId.Trim()}", eventType, message, data, cancellationToken);
        }

        private async Task PublishToGroupAsync(
            string groupName,
            string eventType,
            string message,
            object? data,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(groupName) || string.IsNullOrWhiteSpace(eventType))
            {
                return;
            }

            var payload = new ErpEventMessage
            {
                EventType = eventType.Trim(),
                Message = string.IsNullOrWhiteSpace(message) ? eventType.Trim() : message.Trim(),
                OccurredUtc = DateTime.UtcNow,
                Data = data
            };

            try
            {
                await _hubContext.Clients
                    .Group(groupName)
                    .SendAsync("erp-event", payload, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to publish realtime ERP event '{EventType}' to group '{GroupName}'.",
                    payload.EventType,
                    groupName);
            }
        }

        private sealed class ErpEventMessage
        {
            public string EventType { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public DateTime OccurredUtc { get; set; }
            public object? Data { get; set; }
        }
    }
}
