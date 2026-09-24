using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Refresh;
using JobHunter.Sources;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Tests.Refresh;

/// <summary>Proves that the run history lists the stored runs newest first with their per-source results and grouped scoring failures, and that runs stored before the reasons were kept still read.</summary>
public sealed class RunHistoryServiceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetRunsAsync_WithSeveralRuns_ReturnsThemNewestFirstWithTheirDetails()
    {
        await using RefreshTestHarness harness = new();
        await harness.InitializeAsync();

        FetchRun older = FetchRun.Start(FetchTrigger.Startup, Noon.AddDays(-1));
        older.RecordSourceResult(JobSourceKind.RemoteOk, 99, 4, 95, 81, null);
        older.RecordScoring(3, []);
        older.Complete(Noon.AddDays(-1).AddMinutes(2));

        FetchRun newer = FetchRun.Start(FetchTrigger.Manual, Noon);
        newer.RecordSourceResult(JobSourceKind.WeWorkRemotely, 101, 3, 98, 40, "remote-devops-sysadmin-jobs: 503");
        newer.RecordScoring(0, ["usage limit", "usage limit"]);
        newer.HaltScoring("usage limit");
        newer.Complete(Noon.AddSeconds(35));

        await harness.SaveAsync(older);
        await harness.SaveAsync(newer);

        IReadOnlyList<RefreshRunSummary> runs = await harness.RunHistory.GetRunsAsync();

        Assert.Equal<Guid>([newer.Id, older.Id], [.. runs.Select(run => run.RunId)]);
        RefreshRunSummary latest = runs[0];
        Assert.Equal(TimeSpan.FromSeconds(35), latest.Duration);
        Assert.Equal("remote-devops-sysadmin-jobs: 503", Assert.Single(latest.SourceResults).Error);
        Assert.Equal(new ScoringFailureReason("usage limit", 2), Assert.Single(latest.ScoringFailureReasons));
        Assert.Equal("usage limit", latest.ScoringHaltReason);
        Assert.Empty(runs[1].ScoringFailureReasons);
        Assert.Null(runs[1].ScoringHaltReason);
    }

    [Fact]
    public async Task GetRunsAsync_ForARunStoredBeforeTheReasonsWereKept_ReadsNoReasons()
    {
        await using RefreshTestHarness harness = new();
        await harness.InitializeAsync();
        FetchRun run = FetchRun.Start(FetchTrigger.Startup, Noon);
        run.RecordScoring(0, ["the model refused"]);
        run.Complete(Noon.AddMinutes(1));
        await harness.SaveAsync(run);

        await using (JobHunterDbContext context = await harness.ContextFactory.CreateDbContextAsync())
        {
            await context.Database.ExecuteSqlRawAsync("UPDATE FetchRuns SET ScoringFailureReasons = NULL, ScoringHaltReason = NULL");
        }

        RefreshRunSummary summary = Assert.Single(await harness.RunHistory.GetRunsAsync());

        Assert.Equal(1, summary.ScoreFailures);
        Assert.Empty(summary.ScoringFailureReasons);
        Assert.Null(summary.ScoringHaltReason);
    }

    [Fact]
    public async Task GetRunsAsync_WithMoreRunsThanAsked_ReturnsOnlyTheNewest()
    {
        await using RefreshTestHarness harness = new();
        await harness.InitializeAsync();

        for (int day = 0; day < 3; day++)
        {
            FetchRun run = FetchRun.Start(FetchTrigger.Startup, Noon.AddDays(-day));
            run.Complete(Noon.AddDays(-day).AddMinutes(1));
            await harness.SaveAsync(run);
        }

        IReadOnlyList<RefreshRunSummary> runs = await harness.RunHistory.GetRunsAsync(take: 2);

        Assert.Equal<DateTimeOffset>([Noon, Noon.AddDays(-1)], [.. runs.Select(run => run.StartedAt)]);
    }
}
