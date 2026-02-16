using System.Text.Json;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Integration;

namespace EJCFitnessGym.Services.Integration
{
    public class IntegrationOutboxService : IIntegrationOutbox
    {
        private static readonly JsonSerializerOptions PayloadSerializerOptions = new(JsonSerializerDefaults.Web);

        private readonly ApplicationDbContext _db;

        public IntegrationOutboxService(ApplicationDbContext db)
        {
            _db = db;
        }

        public Task EnqueueBackOfficeAsync(
            string eventType,
            string message,
            object? data = null,
            CancellationToken cancellationToken = default)
        {
            Enqueue(IntegrationOutboxTarget.BackOffice, null, eventType, message, data);
            return Task.CompletedTask;
        }

        public Task EnqueueRoleAsync(
            string role,
            string eventType,
            string message,
            object? data = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                throw new ArgumentException("Role is required.", nameof(role));
            }

            Enqueue(IntegrationOutboxTarget.Role, role.Trim(), eventType, message, data);
            return Task.CompletedTask;
        }

        public Task EnqueueUserAsync(
            string userId,
            string eventType,
            string message,
            object? data = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ArgumentException("User id is required.", nameof(userId));
            }

            Enqueue(IntegrationOutboxTarget.User, userId.Trim(), eventType, message, data);
            return Task.CompletedTask;
        }

        private void Enqueue(
            IntegrationOutboxTarget target,
            string? targetValue,
            string eventType,
            string message,
            object? data)
        {
            if (string.IsNullOrWhiteSpace(eventType))
            {
                throw new ArgumentException("Event type is required.", nameof(eventType));
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException("Message is required.", nameof(message));
            }

            var nowUtc = DateTime.UtcNow;
            _db.IntegrationOutboxMessages.Add(new IntegrationOutboxMessage
            {
                Target = target,
                TargetValue = targetValue,
                EventType = eventType.Trim(),
                Message = message.Trim(),
                PayloadJson = data is null ? null : JsonSerializer.Serialize(data, PayloadSerializerOptions),
                Status = IntegrationOutboxStatus.Pending,
                AttemptCount = 0,
                NextAttemptUtc = nowUtc,
                CreatedUtc = nowUtc,
                UpdatedUtc = nowUtc
            });
        }
    }
}
