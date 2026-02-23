namespace EJCFitnessGym.Services.Staff
{
    public sealed class StaffAttendanceOptions
    {
        public bool AutoCheckoutEnabled { get; set; } = true;
        public int AutoCheckoutHours { get; set; } = 3;
        public int AutoCloseIntervalMinutes { get; set; } = 10;
        public int LookbackDays { get; set; } = 7;
        public int MaxEventsPerSweep { get; set; } = 5000;
        public bool RunOnStartup { get; set; } = true;
    }
}
