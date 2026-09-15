namespace JobHunter.Sources.Dataset;

/// <summary>Registers the applicant tracking system dataset source.</summary>
public static class DatasetRegistration
{
    private static readonly TimeSpan SliceDownloadTimeout = TimeSpan.FromMinutes(30);

    /// <summary>Registers everything the dataset source needs.</summary>
    public static IServiceCollection AddDatasetSource(this IServiceCollection services)
    {
        services.AddHttpClient(DatasetHttp.ClientName, client => client.Timeout = SliceDownloadTimeout);

        return services
            .AddSingleton<ManifestClient>()
            .AddSingleton<SliceDownloader>()
            .AddSingleton<DatasetJobReader>()
            .AddSingleton<IJobSource, DatasetJobSource>();
    }
}
