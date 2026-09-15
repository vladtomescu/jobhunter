using JobHunter.Data;
using JobHunter.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Tests.Pipeline;

/// <summary>Proves that one call to AddPipeline gives the rest of the application every deterministic service it asks for.</summary>
public sealed class PipelineRegistrationTests
{
    [Fact]
    public void AddPipeline_OnAnEmptyCollection_RegistersEveryPipelineService()
    {
        using ServiceProvider provider = BuildProvider();

        Assert.IsType<TitleRules>(provider.GetRequiredService<ITitleRules>());
        Assert.IsType<EcbFxRateProvider>(provider.GetRequiredService<IFxRateProvider>());
        Assert.NotNull(provider.GetRequiredService<Prefilter>());
        Assert.NotNull(provider.GetRequiredService<CompNormalizer>());
        Assert.NotNull(provider.GetRequiredService<ScoreApplier>());
    }

    [Fact]
    public void AddPipeline_OnAnEmptyCollection_RegistersAClientForTheRateProvider()
    {
        using ServiceProvider provider = BuildProvider();

        HttpClient client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(EcbFxRateProvider));

        Assert.Equal(TimeSpan.FromSeconds(30), client.Timeout);
        Assert.Contains("JobHunter/1.0", client.DefaultRequestHeaders.UserAgent.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AddPipeline_ResolvedTwice_ReturnsTheSameRateProvider()
    {
        using ServiceProvider provider = BuildProvider();

        Assert.Same(provider.GetRequiredService<IFxRateProvider>(), provider.GetRequiredService<IFxRateProvider>());
    }

    private static ServiceProvider BuildProvider()
    {
        ServiceCollection services = new();
        services.AddPipeline();
        services.AddSingleton(new DataPaths(Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"))));

        return services.BuildServiceProvider();
    }
}
