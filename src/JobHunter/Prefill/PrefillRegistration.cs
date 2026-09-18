namespace JobHunter.Prefill;

/// <summary>Registers the browser prefill service and the form maps of the supported applicant tracking systems.</summary>
public static class PrefillRegistration
{
    /// <summary>Registers the prefill services, reading from configuration whether prefill attaches to a browser on the desktop or starts one of its own.</summary>
    public static IServiceCollection AddPrefill(this IServiceCollection services)
    {
        services.AddSingleton(provider => PrefillBrowserSource.From(provider.GetRequiredService<IConfiguration>()));
        services.AddSingleton<PrefillService>();

        return services;
    }
}
