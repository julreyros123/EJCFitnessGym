namespace EJCFitnessGym.Services.Integration
{
    public interface IIntegrationOutbox
    {
        Task EnqueueBackOfficeAsync(
            string eventType,
            string message,
            object? data = null,
            CancellationToken cancellationToken = default);

        Task EnqueueRoleAsync(
            string role,
            string eventType,
            string message,
            object? data = null,
            CancellationToken cancellationToken = default);

        Task EnqueueUserAsync(
            string userId,
            string eventType,
            string message,
            object? data = null,
            CancellationToken cancellationToken = default);
    }
}
