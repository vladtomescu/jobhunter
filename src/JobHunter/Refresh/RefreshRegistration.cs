namespace JobHunter.Refresh;

/// <summary>Registers the refresh orchestration, its live state, the run history and the refresh that runs at startup.</summary>
public static class RefreshRegistration
{
    /// <summary>Registers the refresh services.</summary>
    public static IServiceCollection AddRefresh(this IServiceCollection services)
    {
        services.AddSingleton<RefreshState>();
        services.AddSingleton<RefreshService>();
        services.AddSingleton<RunHistoryService>();
        services.AddHostedService<StartupRefreshHostedService>();

        return services;
    }
}
