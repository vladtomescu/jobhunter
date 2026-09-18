using System.Net;
using JobHunter.Prefill;
using Microsoft.Extensions.Configuration;

namespace JobHunter.Tests.Prefill;

/// <summary>Proves which browser prefill works in, and that only a browser prefill started itself is ever closed.</summary>
public class PrefillBrowserSourceTests
{
    [Fact]
    public void From_NoEndpointConfigured_StartsItsOwnBrowser()
    {
        PrefillBrowserSource source = PrefillBrowserSource.From(Configuration(null));

        Assert.Equal(PrefillBrowserMode.Launch, source.Mode);
        Assert.Null(source.Endpoint);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void From_EndpointLeftBlank_StartsItsOwnBrowser(string endpoint)
    {
        PrefillBrowserSource source = PrefillBrowserSource.From(Configuration(endpoint));

        Assert.Equal(PrefillBrowserMode.Launch, source.Mode);
        Assert.Null(source.Endpoint);
    }

    [Fact]
    public void From_EndpointConfigured_AttachesToTheBrowserOnTheDesktop()
    {
        PrefillBrowserSource source = PrefillBrowserSource.From(Configuration("  http://host.docker.internal:9333  "));

        Assert.Equal(PrefillBrowserMode.Connect, source.Mode);
        Assert.Equal("http://host.docker.internal:9333", source.Endpoint);
    }

    [Fact]
    public void ClosesOnShutdown_BrowserItStartedItself_IsClosedWithTheApplication()
    {
        Assert.True(PrefillBrowserSource.From(Configuration(null)).ClosesOnShutdown);
    }

    [Fact]
    public void ClosesOnShutdown_BrowserOnTheDesktop_IsLeftRunning()
    {
        Assert.False(PrefillBrowserSource.From(Configuration("http://host.docker.internal:9333")).ClosesOnShutdown);
    }

    [Fact]
    public async Task ResolveEndpointAsync_HostName_IsReplacedByAnAddressWithThePortKept()
    {
        string endpoint = await PrefillBrowserSource.From(Configuration("http://localhost:9333")).ResolveEndpointAsync(CancellationToken.None);

        Uri resolved = new(endpoint, UriKind.Absolute);

        Assert.True(IPAddress.TryParse(resolved.Host.Trim('[', ']'), out IPAddress? _), $"{endpoint} does not carry an address.");
        Assert.Equal(9333, resolved.Port);
        Assert.EndsWith(":9333", endpoint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolveEndpointAsync_AddressAlready_IsLeftAsItIs()
    {
        string endpoint = await PrefillBrowserSource.From(Configuration("http://192.168.65.254:9333")).ResolveEndpointAsync(CancellationToken.None);

        Assert.Equal("http://192.168.65.254:9333", endpoint);
    }

    [Fact]
    public async Task ResolveEndpointAsync_EndpointCarriesAPath_KeepsIt()
    {
        string endpoint = await PrefillBrowserSource.From(Configuration("http://127.0.0.1:9333/bridge")).ResolveEndpointAsync(CancellationToken.None);

        Assert.Equal("http://127.0.0.1:9333/bridge", endpoint);
    }

    [Fact]
    public async Task ResolveEndpointAsync_PrefillStartsItsOwnBrowser_SaysThereIsNothingToResolve()
    {
        PrefillBrowserSource source = PrefillBrowserSource.From(Configuration(null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => source.ResolveEndpointAsync(CancellationToken.None));
    }

    private static IConfiguration Configuration(string? endpoint)
    {
        Dictionary<string, string?> values = endpoint is null ? [] : new Dictionary<string, string?> { [PrefillBrowserSource.EndpointKey] = endpoint };

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
