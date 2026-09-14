using JobHunter.Sources.Dataset;
using JobHunter.Sources.RemoteOk;
using JobHunter.Sources.WeWorkRemotely;

namespace JobHunter.Sources;

/// <summary>Registers every job source through the registration file that belongs to that source.</summary>
public static class SourcesRegistration
{
    /// <summary>Registers the job sources.</summary>
    public static IServiceCollection AddSources(this IServiceCollection services)
    {
        return services
            .AddRemoteOkSource()
            .AddWeWorkRemotelySource()
            .AddDatasetSource();
    }
}
