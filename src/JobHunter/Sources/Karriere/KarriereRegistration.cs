using JobHunter.Sources;

namespace JobHunter.Sources.Karriere;

/// <summary>Registers the karriere.at source.</summary>
public static class KarriereRegistration
{
    /// <summary>The pause between two karriere.at requests.</summary>
    private static readonly TimeSpan PauseBetweenRequests = TimeSpan.FromSeconds(1);

    /// <summary>Registers everything the karriere.at source needs.</summary>
    public static IServiceCollection AddKarriereSource(this IServiceCollection services)
    {
        services.AddSingleton(new KarrierePacing(PauseBetweenRequests));
        services.AddHttpClient<KarriereSource>();
        services.AddTransient<IJobSource>(provider => provider.GetRequiredService<KarriereSource>());

        return services;
    }
}
