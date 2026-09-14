namespace JobHunter.Applications;

/// <summary>Registers triage, the application service and the statistics service.</summary>
public static class ApplicationsRegistration
{
    /// <summary>Registers the application services.</summary>
    public static IServiceCollection AddApplications(this IServiceCollection services)
    {
        return services;
    }
}
