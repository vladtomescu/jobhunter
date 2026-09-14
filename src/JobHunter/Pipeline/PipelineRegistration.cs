namespace JobHunter.Pipeline;

/// <summary>Registers the deterministic pipeline: normalization, deduplication, title and geography rules, prefilter, compensation and classification.</summary>
public static class PipelineRegistration
{
    /// <summary>Registers the pipeline services.</summary>
    public static IServiceCollection AddPipeline(this IServiceCollection services)
    {
        return services;
    }
}
