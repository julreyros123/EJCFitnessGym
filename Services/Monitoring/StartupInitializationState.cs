namespace EJCFitnessGym.Services.Monitoring
{
    public class StartupInitializationState
    {
        public bool HasFailure { get; private set; }

        public string? FailureMessage { get; private set; }

        public Exception? FailureException { get; private set; }

        public void ReportFailure(string message, Exception? exception = null)
        {
            if (HasFailure)
            {
                return;
            }

            HasFailure = true;
            FailureMessage = message;
            FailureException = exception;
        }
    }
}
