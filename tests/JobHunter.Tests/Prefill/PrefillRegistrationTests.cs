using JobHunter.Data;
using JobHunter.Prefill;
using JobHunter.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Tests.Prefill;

/// <summary>Proves that one call to AddPrefill gives the application a prefill service whose browser is the one configuration asks for.</summary>
public class PrefillRegistrationTests
{
    [Fact]
    public async Task AddPrefill_NoEndpointConfigured_LeavesPrefillStartingItsOwnBrowser()
    {
        await using ServiceProvider provider = BuildProvider(null);

        Assert.Equal(PrefillBrowserMode.Launch, provider.GetRequiredService<PrefillBrowserSource>().Mode);
    }

    [Fact]
    public async Task AddPrefill_EndpointConfigured_MakesPrefillAttachToTheBrowserOnTheDesktop()
    {
        await using ServiceProvider provider = BuildProvider("http://host.docker.internal:9333");

        PrefillBrowserSource source = provider.GetRequiredService<PrefillBrowserSource>();

        Assert.Equal(PrefillBrowserMode.Connect, source.Mode);
        Assert.Equal("http://host.docker.internal:9333", source.Endpoint);
    }

    [Fact]
    public async Task AddPrefill_ResolvedTwice_ReturnsTheSameServiceSoOneBrowserIsShared()
    {
        await using ServiceProvider provider = BuildProvider(null);

        Assert.Same(provider.GetRequiredService<PrefillService>(), provider.GetRequiredService<PrefillService>());
    }

    [Fact]
    public async Task DisposeAsync_InConnectModeBeforeAnyPrefill_LeavesTheDesktopBrowserAloneAndCompletes()
    {
        ServiceProvider provider = BuildProvider("http://host.docker.internal:9333");
        PrefillBrowserSource source = provider.GetRequiredService<PrefillBrowserSource>();
        provider.GetRequiredService<PrefillService>();

        await provider.DisposeAsync();

        Assert.False(source.ClosesOnShutdown);
    }

    private static ServiceProvider BuildProvider(string? endpoint)
    {
        Dictionary<string, string?> values = endpoint is null ? [] : new Dictionary<string, string?> { [PrefillBrowserSource.EndpointKey] = endpoint };

        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        services.AddData();
        services.AddSettings();
        services.AddPrefill();
        services.AddSingleton(new DataPaths(Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"))));

        return services.BuildServiceProvider();
    }
}
