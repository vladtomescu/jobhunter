namespace JobHunter.Jobs;

/// <summary>Registers the read models behind the inbox and the job lists, the manual job service and the old-job retention.</summary>
public static class JobsRegistration
{
    /// <summary>Registers the job services.</summary>
    public static IServiceCollection AddJobs(this IServiceCollection services)
    {
        services.AddSingleton<JobQueryService>();
        services.AddSingleton<ManualJobService>();
        services.AddSingleton<JobRetentionService>();

        return services;
    }
}
