namespace JobHunter.Jobs;

/// <summary>Registers the read models behind the inbox and the job lists, and the manual job service.</summary>
public static class JobsRegistration
{
    /// <summary>Registers the job services.</summary>
    public static IServiceCollection AddJobs(this IServiceCollection services)
    {
        return services;
    }
}
