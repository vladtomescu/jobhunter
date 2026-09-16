using JobHunter.Domain;
using JobHunter.Refresh;
using Xunit.Abstractions;

namespace JobHunter.Tests.Refresh;

/// <summary>Runs one whole refresh against the live RemoteOK and We Work Remotely feeds, with the dataset switched off and the fake scorer standing in for a paid call.</summary>
public sealed class RefreshLiveTests(ITestOutputHelper output)
{
    [LiveFact]
    public async Task RunAsync_AgainstTheLiveFeeds_InsertsPostingsAndCountsThemPerSource()
    {
        await using RefreshTestHarness harness = RefreshTestHarness.WithLiveSources();
        await harness.InitializeAsync();
        await harness.Settings.ApplyAsync(settings =>
        {
            settings.ConfigureSources(remoteOkEnabled: true, wwrEnabled: true, datasetEnabled: false, datasetAtsList: string.Empty);
            settings.ConfigureRunLimits(21, 21, 12, 5);
        });

        RefreshResult result = await harness.RunAsync();

        Assert.NotNull(result.Summary);

        foreach (SourceRunResult line in result.Summary.SourceResults)
        {
            output.WriteLine($"{line.Kind}: fetched {line.Fetched}, new {line.New}, updated {line.Updated}, dropped {line.Dropped}, error {line.Error ?? "none"}");
        }

        List<Job> stored = await harness.JobsAsync();
        output.WriteLine($"stored {stored.Count} jobs, scored {result.Summary.Scored}, scoring failures {result.Summary.ScoreFailures}");

        Assert.Equal(2, result.Summary.SourceResults.Count);
        Assert.True(result.Summary.NewJobs >= 1, "the live feeds returned no posting worth storing");
        Assert.NotEmpty(stored);
    }
}
