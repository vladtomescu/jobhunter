using JobHunter.Sources;

namespace JobHunter.Sources.WeWorkRemotely;

/// <summary>Registers the We Work Remotely source.</summary>
public static class WeWorkRemotelyRegistration
{
    /// <summary>Registers everything the We Work Remotely source needs.</summary>
    public static IServiceCollection AddWeWorkRemotelySource(this IServiceCollection services)
    {
        services.AddHttpClient<WeWorkRemotelySource>();
        services.AddTransient<IJobSource>(provider => provider.GetRequiredService<WeWorkRemotelySource>());

        return services;
    }
}
