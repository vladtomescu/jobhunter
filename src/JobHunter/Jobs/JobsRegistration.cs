namespace JobHunter.Jobs;

/// <summary>Registers the read models behind the inbox and the job lists, the manual job service, the old-job retention, the pay recompute and the candidate flag recompute.</summary>
public static class JobsRegistration
{
    /// <summary>Registers the job services.</summary>
    public static IServiceCollection AddJobs(this IServiceCollection services)
    {
        services.AddSingleton<JobQueryService>();
        services.AddSingleton<CompRecomputeService>();
        services.AddSingleton<ManualJobService>();
        services.AddSingleton<JobRetentionService>();
        services.AddSingleton<CandidateFlagRecompute>();

        return services;
    }
}
