using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.HttpOverrides;

namespace EJCFitnessGym.Services.Monitoring
{
    public static class ForwardedHeadersSecurityConfigurator
    {
        public static ForwardedHeadersOptions CreateOptions(
            ForwardedHeadersSecurityOptions settings,
            bool isDevelopment)
        {
            var options = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
                RequireHeaderSymmetry = settings.RequireHeaderSymmetry
            };

            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();

            if (settings.ForwardLimit is > 0)
            {
                options.ForwardLimit = settings.ForwardLimit;
            }

            var trustedEntryCount = 0;

            foreach (var proxy in settings.KnownProxies)
            {
                var proxyValue = proxy?.Trim();
                if (string.IsNullOrWhiteSpace(proxyValue))
                {
                    continue;
                }

                if (!IPAddress.TryParse(proxyValue, out var proxyAddress))
                {
                    throw new InvalidOperationException(
                        $"ForwardedHeaders:KnownProxies contains an invalid IP address: '{proxyValue}'.");
                }

                options.KnownProxies.Add(proxyAddress);
                trustedEntryCount++;
            }

            foreach (var network in settings.KnownNetworks)
            {
                var networkValue = network?.Trim();
                if (string.IsNullOrWhiteSpace(networkValue))
                {
                    continue;
                }

                options.KnownNetworks.Add(ParseNetwork(networkValue));
                trustedEntryCount++;
            }

            if (trustedEntryCount == 0)
            {
                if (isDevelopment)
                {
                    options.KnownProxies.Add(IPAddress.Loopback);
                    options.KnownProxies.Add(IPAddress.IPv6Loopback);
                    return options;
                }

                throw new InvalidOperationException(
                    "ForwardedHeaders:Enabled requires at least one trusted proxy or network outside Development.");
            }

            return options;
        }

        private static Microsoft.AspNetCore.HttpOverrides.IPNetwork ParseNetwork(string value)
        {
            var parts = value.Split('/', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 ||
                !IPAddress.TryParse(parts[0], out var prefix) ||
                !int.TryParse(parts[1], out var prefixLength))
            {
                throw new InvalidOperationException(
                    $"ForwardedHeaders:KnownNetworks contains an invalid CIDR network: '{value}'.");
            }

            var maxPrefixLength = prefix.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
            if (prefixLength < 0 || prefixLength > maxPrefixLength)
            {
                throw new InvalidOperationException(
                    $"ForwardedHeaders:KnownNetworks contains an invalid prefix length: '{value}'.");
            }

            return new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, prefixLength);
        }
    }
}
