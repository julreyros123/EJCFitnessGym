namespace EJCFitnessGym.Security
{
    public sealed class RateLimitingOptions
    {
        public const string PolicyName = "StrictAuthLimit";
        public const string AnonymousPolicy = "AnonymousLimit";

        public int PermitLimit { get; set; } = 5;
        public int WindowSeconds { get; set; } = 60;
        public int QueueLimit { get; set; } = 0;
    }
}
