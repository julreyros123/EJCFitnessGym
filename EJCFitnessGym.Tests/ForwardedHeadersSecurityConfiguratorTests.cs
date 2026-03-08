using System.Net;
using EJCFitnessGym.Services.Monitoring;

namespace EJCFitnessGym.Tests;

#pragma warning disable ASPDEPR005
public class ForwardedHeadersSecurityConfiguratorTests
{
    [Fact]
    public void CreateOptions_ProductionWithoutTrustedEntries_Throws()
    {
        var settings = new ForwardedHeadersSecurityOptions
        {
            Enabled = true
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ForwardedHeadersSecurityConfigurator.CreateOptions(settings, isDevelopment: false));

        Assert.Contains("trusted proxy or network", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateOptions_DevelopmentWithoutTrustedEntries_AddsLoopbackProxies()
    {
        var settings = new ForwardedHeadersSecurityOptions
        {
            Enabled = true
        };

        var options = ForwardedHeadersSecurityConfigurator.CreateOptions(settings, isDevelopment: true);

        Assert.Contains(IPAddress.Loopback, options.KnownProxies);
        Assert.Contains(IPAddress.IPv6Loopback, options.KnownProxies);
    }

    [Fact]
    public void CreateOptions_WithTrustedEntries_ParsesProxiesAndNetworks()
    {
        var settings = new ForwardedHeadersSecurityOptions
        {
            Enabled = true,
            KnownProxies = ["203.0.113.10"],
            KnownNetworks = ["10.0.0.0/24"],
            ForwardLimit = 2,
            RequireHeaderSymmetry = true
        };

        var options = ForwardedHeadersSecurityConfigurator.CreateOptions(settings, isDevelopment: false);

        Assert.Contains(IPAddress.Parse("203.0.113.10"), options.KnownProxies);
        Assert.Single(options.KnownNetworks);
        Assert.Equal(2, options.ForwardLimit);
        Assert.True(options.RequireHeaderSymmetry);
    }
}
#pragma warning restore ASPDEPR005
