namespace EJCFitnessGym.Services.Staff
{
    public interface IStaffAttendanceService
    {
        TimeSpan AutoCheckoutAfter { get; }

        bool IsSessionTimedOut(DateTime checkInUtc, DateTime? asOfUtc = null);

        Task<int> AutoCloseStaleSessionsAsync(string? branchId = null, CancellationToken cancellationToken = default);
    }
}
