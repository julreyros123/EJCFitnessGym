namespace EJCFitnessGym.Services.Memberships
{
    public class MembershipLifecycleWorkerOptions
    {
        public bool Enabled { get; set; } = true;
        public bool RunOnStartup { get; set; } = true;
        public int IntervalMinutes { get; set; } = 60;
        public bool PublishRealtimeWhenChangesDetected { get; set; } = true;
    }
}
