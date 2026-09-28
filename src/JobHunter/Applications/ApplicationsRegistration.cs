namespace JobHunter.Applications;

/// <summary>Registers triage, the application service, the cover-letter service and the statistics service.</summary>
public static class ApplicationsRegistration
{
    /// <summary>Registers the application services.</summary>
    public static IServiceCollection AddApplications(this IServiceCollection services)
    {
        services.AddSingleton<TriageService>();
        services.AddSingleton<ApplicationService>();
        services.AddSingleton<CoverLetterService>();
        services.AddSingleton<GhostCandidateQuery>();
        services.AddSingleton<StatsService>();

        return services;
    }
}
