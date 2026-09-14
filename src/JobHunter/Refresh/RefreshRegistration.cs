namespace JobHunter.Refresh;

/// <summary>Registers the refresh orchestration, its live state and the refresh that runs at startup.</summary>
public static class RefreshRegistration
{
    /// <summary>Registers the refresh services.</summary>
    public static IServiceCollection AddRefresh(this IServiceCollection services)
    {
        return services;
    }
}
