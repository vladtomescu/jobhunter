using JobHunter.Data;
using JobHunter.Pipeline;
using JobHunter.Sources;
using JobHunter.Sources.Dataset;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace JobHunter.Tests.Sources;

/// <summary>Reaches the live dataset host: reads the manifest, pulls the smallest slice into the repository data folder, and reads every slice the settings turn on.</summary>
public sealed class DatasetLiveTests(ITestOutputHelper output)
{
    private const string SmallestAts = "manfred";

    [LiveFact]
    public async Task GetAsync_AgainstTheLiveManifest_ListsASliceForEveryConfiguredApplicantTrackingSystem()
    {
        await using ServiceProvider provider = BuildProvider();

        DatasetManifest manifest = await provider.GetRequiredService<ManifestClient>().GetAsync(SourceUserAgent.Build(string.Empty), CancellationToken.None);

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
        SourceFetchContext context = new(DateTimeOffset.UtcNow.AddYears(-10), new CandidateProfile(null, true, true, ["en", "nl"], [], [], []), Path.Combine(RepositoryDataFolder(), "raw"), NewSettings());

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

    [LiveFact]
    public async Task FetchAsync_ForEveryConfiguredSlice_ReadsEverySliceWithoutAnError()
    {
        await using ServiceProvider provider = BuildProvider();
        IJobSource source = provider.GetRequiredService<IJobSource>();
        CandidateProfile candidate = CandidateProfile.FromSettings(NewSettings());

        foreach (string ats in ConfiguredAts())
        {
            SourceFetchContext context = new(DateTimeOffset.UtcNow.AddDays(-21), candidate, Path.Combine(RepositoryDataFolder(), "raw"), NewSettings(ats));

            SourceFetchResult result = await source.FetchAsync(context, CancellationToken.None);

            output.WriteLine($"{ats}: {result.Jobs.Count} postings kept of {result.FetchedCount} rows in the slice, error: {result.Error ?? "none"}");

            Assert.Null(result.Error);
            Assert.NotEmpty(result.Jobs);
            Assert.All(result.Jobs, job => Assert.Equal(ats, job.Ats));
        }
    }

    /// <summary>The applicant tracking systems the settings turn on by default, which is what a refresh reads.</summary>
    private static IEnumerable<string> ConfiguredAts()
    {
        return JobHunter.Domain.Settings.CreateDefault().DatasetAtsList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static JobHunter.Domain.Settings NewSettings(string ats = SmallestAts)
    {
        JobHunter.Domain.Settings settings = JobHunter.Domain.Settings.CreateDefault();
        settings.ConfigureSources(remoteOkEnabled: false, wwrEnabled: false, datasetEnabled: true, ats);

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
}
