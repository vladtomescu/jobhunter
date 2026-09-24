using JobHunter.Sources;

namespace JobHunter.Pipeline;

/// <summary>Registers the deterministic pipeline: normalization, deduplication, title and geography rules, prefilter, compensation and classification.</summary>
public static class PipelineRegistration
{
    /// <summary>Registers the pipeline services.</summary>
    public static IServiceCollection AddPipeline(this IServiceCollection services)
    {
        services.AddHttpClient(nameof(EcbFxRateProvider), client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(SourceUserAgent.Build(string.Empty));
        });

        services.AddSingleton<ITitleRules, TitleRules>();
        services.AddSingleton<Prefilter>();
        services.AddSingleton<IFxRateProvider, EcbFxRateProvider>();
        services.AddSingleton<CompNormalizer>();
        services.AddSingleton<ScoreApplier>();

        return services;
    }
}
