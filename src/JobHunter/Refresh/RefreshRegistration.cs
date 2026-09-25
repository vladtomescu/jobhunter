namespace JobHunter.Refresh;

/// <summary>Registers the refresh and the score run with the gate they share, their live state, the count of jobs waiting for a score, the scoring of one job shared with the job page, the run history and the refresh that runs at startup.</summary>
public static class RefreshRegistration
{
    /// <summary>Registers the refresh services.</summary>
    public static IServiceCollection AddRefresh(this IServiceCollection services)
    {
        services.AddSingleton<RefreshState>();
        services.AddSingleton<RunGate>();
        services.AddSingleton<ScoreBacklog>();
        services.AddSingleton<JobScoringStep>();
        services.AddSingleton<JobRescoreService>();
        services.AddSingleton<RefreshService>();
        services.AddSingleton<ScoreRunService>();
        services.AddSingleton<RunHistoryService>();
        services.AddHostedService<StartupRefreshHostedService>();

        return services;
    }
}
