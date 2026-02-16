using System.Text.Json;
using EJCFitnessGym.Data;
using EJCFitnessGym.Models.Integration;
using EJCFitnessGym.Services.Realtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EJCFitnessGym.Services.Integration
{
    public class IntegrationOutboxDispatcherWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IOptionsMonitor<IntegrationOutboxDispatcherOptions> _options;
        private readonly ILogger<IntegrationOutboxDispatcherWorker> _logger;

        public IntegrationOutboxDispatcherWorker(
            IServiceScopeFactory scopeFactory,
            IOptionsMonitor<IntegrationOutboxDispatcherOptions> options,
            ILogger<IntegrationOutboxDispatcherWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _options = options;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_options.CurrentValue.Enabled)
                {
                    try
                    {
                        await DispatchBatchAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Integration outbox dispatch cycle failed.");
                    }
                }

                var pollSeconds = Math.Clamp(_options.CurrentValue.PollIntervalSeconds, 1, 300);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        private async Task DispatchBatchAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var realtimePublisher = scope.ServiceProvider.GetRequiredService<IErpEventPublisher>();

            var nowUtc = DateTime.UtcNow;
            var batchSize = Math.Clamp(_options.CurrentValue.BatchSize, 1, 500);
            var dueMessages = await db.IntegrationOutboxMessages
                .Where(m =>
                    (m.Status == IntegrationOutboxStatus.Pending || m.Status == IntegrationOutboxStatus.Processing) &&
                    m.NextAttemptUtc <= nowUtc)
                .OrderBy(m => m.CreatedUtc)
                .ThenBy(m => m.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            if (dueMessages.Count == 0)
            {
                return;
            }

            foreach (var message in dueMessages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                message.Status = IntegrationOutboxStatus.Processing;
                message.AttemptCount++;
                message.LastAttemptUtc = nowUtc;
                message.UpdatedUtc = nowUtc;
                await db.SaveChangesAsync(cancellationToken);

                try
                {
                    var payload = DeserializePayload(message.PayloadJson);
                    await PublishAsync(realtimePublisher, message, payload, cancellationToken);

                    message.Status = IntegrationOutboxStatus.Processed;
                    message.ProcessedUtc = DateTime.UtcNow;
                    message.LastError = null;
                    message.UpdatedUtc = message.ProcessedUtc.Value;
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    var maxAttempts = Math.Clamp(_options.CurrentValue.MaxAttempts, 1, 100);
                    var baseRetryDelaySeconds = Math.Clamp(_options.CurrentValue.BaseRetryDelaySeconds, 1, 600);
                    var exhausted = message.AttemptCount >= maxAttempts;
                    var delaySeconds = baseRetryDelaySeconds * (int)Math.Pow(2, Math.Min(message.AttemptCount - 1, 6));

                    message.Status = exhausted ? IntegrationOutboxStatus.Failed : IntegrationOutboxStatus.Pending;
                    message.NextAttemptUtc = DateTime.UtcNow.AddSeconds(delaySeconds);
                    message.LastError = Trim(ex.Message, 2000);
                    message.UpdatedUtc = DateTime.UtcNow;

                    await db.SaveChangesAsync(cancellationToken);

                    if (exhausted)
                    {
                        _logger.LogError(
                            ex,
                            "Integration outbox message {OutboxMessageId} failed permanently after {AttemptCount} attempts.",
                            message.Id,
                            message.AttemptCount);
                    }
                    else
                    {
                        _logger.LogWarning(
                            ex,
                            "Integration outbox message {OutboxMessageId} failed on attempt {AttemptCount}; retry scheduled.",
                            message.Id,
                            message.AttemptCount);
                    }
                }
            }
        }

        private static async Task PublishAsync(
            IErpEventPublisher publisher,
            IntegrationOutboxMessage message,
            object? payload,
            CancellationToken cancellationToken)
        {
            switch (message.Target)
            {
                case IntegrationOutboxTarget.BackOffice:
                    await publisher.PublishToBackOfficeAsync(
                        message.EventType,
                        message.Message,
                        payload,
                        cancellationToken);
                    break;
                case IntegrationOutboxTarget.Role:
                    await publisher.PublishToRoleAsync(
                        message.TargetValue ?? string.Empty,
                        message.EventType,
                        message.Message,
                        payload,
                        cancellationToken);
                    break;
                case IntegrationOutboxTarget.User:
                    await publisher.PublishToUserAsync(
                        message.TargetValue ?? string.Empty,
                        message.EventType,
                        message.Message,
                        payload,
                        cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported outbox target '{message.Target}'.");
            }
        }

        private static object? DeserializePayload(string? payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<JsonElement>(payloadJson);
            }
            catch
            {
                return payloadJson;
            }
        }

        private static string Trim(string value, int maxLength)
        {
            return value.Length <= maxLength ? value : value[..maxLength];
        }
    }
}
