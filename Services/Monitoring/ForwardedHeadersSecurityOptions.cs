namespace EJCFitnessGym.Services.Monitoring
{
    public class ForwardedHeadersSecurityOptions
    {
        public bool Enabled { get; set; }

        public int? ForwardLimit { get; set; } = 1;

        public bool RequireHeaderSymmetry { get; set; } = true;

        public string[] KnownProxies { get; set; } = Array.Empty<string>();

        public string[] KnownNetworks { get; set; } = Array.Empty<string>();
    }
}
