namespace EJCFitnessGym.Services.Realtime
{
    public interface IErpEventPublisher
    {
        Task PublishToBackOfficeAsync(
            string eventType,
            string message,
            object? data = null,
            CancellationToken cancellationToken = default);

        Task PublishToRoleAsync(
            string role,
            string eventType,
            string message,
            object? data = null,
            CancellationToken cancellationToken = default);

        Task PublishToUserAsync(
            string userId,
            string eventType,
            string message,
            object? data = null,
            CancellationToken cancellationToken = default);
    }
}
