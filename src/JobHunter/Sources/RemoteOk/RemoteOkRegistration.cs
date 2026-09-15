using JobHunter.Sources;

namespace JobHunter.Sources.RemoteOk;

/// <summary>Registers the RemoteOK source.</summary>
public static class RemoteOkRegistration
{
    /// <summary>Registers everything the RemoteOK source needs.</summary>
    public static IServiceCollection AddRemoteOkSource(this IServiceCollection services)
    {
        services.AddHttpClient<RemoteOkSource>();
        services.AddTransient<IJobSource>(provider => provider.GetRequiredService<RemoteOkSource>());

        return services;
    }
}
