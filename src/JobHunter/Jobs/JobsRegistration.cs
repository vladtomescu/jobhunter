namespace JobHunter.Jobs;

/// <summary>Registers the read models behind the inbox and the job lists, the manual job service, the old-job retention, the pay recompute, the candidate flag recompute, the location recompute, the copy folder, the excluded-company sweep, the skill service and the read model behind the skills page.</summary>
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
        services.AddSingleton<LocationRecompute>();
        services.AddSingleton<CopyFolder>();
        services.AddSingleton<ExcludedCompanySweep>();
        services.AddSingleton<SkillService>();
        services.AddSingleton<SkillQueryService>();

        return services;
    }
}
