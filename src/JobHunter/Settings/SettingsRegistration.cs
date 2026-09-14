namespace JobHunter.Settings;

/// <summary>Registers the settings service.</summary>
public static class SettingsRegistration
{
    /// <summary>Registers settings access.</summary>
    public static IServiceCollection AddSettings(this IServiceCollection services)
    {
        services.AddSingleton<SettingsService>();

        return services;
    }
}
