using JobHunter.Data;
using JobHunter.Pipeline;
using JobHunter.Sources;
using JobHunter.Sources.Dataset;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Tests.Sources;

/// <summary>Reaches the live dataset host: reads the manifest and pulls the smallest slice into the repository data folder.</summary>
public sealed class DatasetLiveTests
{
    private const string SmallestAts = "manfred";

    [LiveFact]
    public async Task GetAsync_AgainstTheLiveManifest_ListsASliceForEveryConfiguredApplicantTrackingSystem()
    {
        await using ServiceProvider provider = BuildProvider();

        DatasetManifest manifest = await provider.GetRequiredService<ManifestClient>().GetAsync("JobHunter/1.0 (personal)", CancellationToken.None);

        Assert.NotEmpty(manifest.Version);
        Assert.True(manifest.Slices.ContainsKey(SmallestAts));

        foreach (string ats in new[] { "greenhouse", "lever", "ashby", "workable" })
        {
            DatasetSlice slice = manifest.Slices[ats];

            Assert.StartsWith("https://", slice.ParquetUrl, StringComparison.Ordinal);
            Assert.Equal(64, slice.ParquetSha256.Length);
            Assert.True(slice.Rows > 0);
        }
    }

    [LiveFact]
    public async Task FetchAsync_ForTheSmallestSlice_DownloadsItOnceAndReadsPostingsOnEveryRun()
    {
        await using ServiceProvider provider = BuildProvider();
        IJobSource source = provider.GetRequiredService<IJobSource>();
        SourceFetchContext context = new(DateTimeOffset.UtcNow.AddYears(-10), new AnyTitleRules(), Path.Combine(RepositoryDataFolder(), "raw"), NewSettings());

        SourceFetchResult first = await source.FetchAsync(context, CancellationToken.None);

        Assert.Null(first.Error);
        Assert.True(first.FetchedCount > 0);
        Assert.NotEmpty(first.Jobs);
        Assert.All(first.Jobs, job => Assert.Equal(JobSourceKind.Dataset, job.Source));
        Assert.All(first.Jobs, job => Assert.Equal(SmallestAts, job.Ats));
        Assert.All(first.Jobs, job => Assert.NotEmpty(job.PostingUrl));

        string slicePath = Path.Combine(RepositoryDataFolder(), "dataset", SmallestAts, "jobs.parquet");

        Assert.True(File.Exists(slicePath));
        Assert.True(File.Exists(slicePath + ".sha256"));

        DateTime writtenAt = File.GetLastWriteTimeUtc(slicePath);

        SourceFetchResult second = await source.FetchAsync(context, CancellationToken.None);

        Assert.Null(second.Error);
        Assert.Equal(first.Jobs.Count, second.Jobs.Count);
        Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(slicePath));
    }

    private static JobHunter.Domain.Settings NewSettings()
    {
        JobHunter.Domain.Settings settings = JobHunter.Domain.Settings.CreateDefault();
        settings.ConfigureSources(remoteOkEnabled: false, wwrEnabled: false, datasetEnabled: true, SmallestAts);

        return settings;
    }

    private static ServiceProvider BuildProvider()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(new DataPaths(RepositoryDataFolder()));
        services.AddDatasetSource();

        return services.BuildServiceProvider();
    }

    private static string RepositoryDataFolder()
    {
        DirectoryInfo? folder = new(AppContext.BaseDirectory);

        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "JobHunter.slnx")))
        {
            folder = folder.Parent;
        }

        return folder is null
            ? throw new InvalidOperationException("The repository root carrying JobHunter.slnx was not found above the test output folder.")
            : Path.Combine(folder.FullName, "data");
    }

    private sealed class AnyTitleRules : ITitleRules
    {
        public TitleVerdict Evaluate(string title)
        {
            return TitleVerdict.Included;
        }
    }
}
