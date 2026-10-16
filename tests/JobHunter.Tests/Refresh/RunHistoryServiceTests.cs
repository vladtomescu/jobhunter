using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Jobs;
using JobHunter.Refresh;
using JobHunter.Sources;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Tests.Refresh;

/// <summary>Proves that the run history pages the stored runs newest first, fifty to a page, with the total count and with their per-source results and grouped scoring failures, and that runs stored before the reasons were kept still read.</summary>
public sealed class RunHistoryServiceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetPageAsync_WithSeveralRuns_ReturnsThemNewestFirstWithTheirDetails()
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

        RunHistoryPage page = await harness.RunHistory.GetPageAsync();
        IReadOnlyList<RefreshRunSummary> runs = page.Runs;

        Assert.Equal<Guid>([newer.Id, older.Id], [.. runs.Select(run => run.RunId)]);
        Assert.Equal(2, page.TotalCount);
        RefreshRunSummary latest = runs[0];
        Assert.Equal(TimeSpan.FromSeconds(35), latest.Duration);
        Assert.Equal("remote-devops-sysadmin-jobs: 503", Assert.Single(latest.SourceResults).Error);
        Assert.Equal(new ScoringFailureReason("usage limit", 2), Assert.Single(latest.ScoringFailureReasons));
        Assert.Equal("usage limit", latest.ScoringHaltReason);
        Assert.Empty(runs[1].ScoringFailureReasons);
        Assert.Null(runs[1].ScoringHaltReason);
    }

    [Fact]
    public async Task GetPageAsync_ForARunStoredBeforeTheReasonsWereKept_ReadsNoReasons()
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

        RefreshRunSummary summary = Assert.Single((await harness.RunHistory.GetPageAsync()).Runs);

        Assert.Equal(1, summary.ScoreFailures);
        Assert.Empty(summary.ScoringFailureReasons);
        Assert.Null(summary.ScoringHaltReason);
    }

    [Fact]
    public async Task GetPageAsync_WithFewerRunsThanAPage_ReturnsThemAllOnTheFirstPage()
    {
        await using RefreshTestHarness harness = new();
        await harness.InitializeAsync();
        await SaveRunsAsync(harness, 3);

        RunHistoryPage page = await harness.RunHistory.GetPageAsync();

        Assert.Equal<DateTimeOffset>([Noon, Noon.AddMinutes(-1), Noon.AddMinutes(-2)], [.. page.Runs.Select(run => run.StartedAt)]);
        Assert.Equal(0, page.PageIndex);
        Assert.Equal(3, page.TotalCount);
    }

    [Fact]
    public async Task GetPageAsync_WithMoreRunsThanAPage_ReturnsTheNewestFiftyAndTheCountOfAll()
    {
        await using RefreshTestHarness harness = new();
        await harness.InitializeAsync();
        await SaveRunsAsync(harness, 120);

        RunHistoryPage page = await harness.RunHistory.GetPageAsync();

        Assert.Equal(50, Paging.PageSize);
        Assert.Equal(50, page.Runs.Count);
        Assert.Equal(Noon, page.Runs[0].StartedAt);
        Assert.Equal(Noon.AddMinutes(-49), page.Runs[^1].StartedAt);
        Assert.Equal(0, page.PageIndex);
        Assert.Equal(120, page.TotalCount);
    }

    [Fact]
    public async Task GetPageAsync_ForTheSecondPage_ReturnsTheNextFiftyRunsWithoutRepeatingTheFirst()
    {
        await using RefreshTestHarness harness = new();
        await harness.InitializeAsync();
        await SaveRunsAsync(harness, 120);

        RunHistoryPage first = await harness.RunHistory.GetPageAsync(pageIndex: 0);
        RunHistoryPage second = await harness.RunHistory.GetPageAsync(pageIndex: 1);

        Assert.Equal(50, second.Runs.Count);
        Assert.Equal(Noon.AddMinutes(-50), second.Runs[0].StartedAt);
        Assert.Equal(Noon.AddMinutes(-99), second.Runs[^1].StartedAt);
        Assert.Empty(first.Runs.Select(run => run.RunId).Intersect(second.Runs.Select(run => run.RunId)));
        Assert.Equal(1, second.PageIndex);
        Assert.Equal(120, second.TotalCount);
    }

    [Fact]
    public async Task GetPageAsync_ForTheLastPage_ReturnsTheRemainingRuns()
    {
        await using RefreshTestHarness harness = new();
        await harness.InitializeAsync();
        await SaveRunsAsync(harness, 120);

        RunHistoryPage page = await harness.RunHistory.GetPageAsync(pageIndex: 2);

        Assert.Equal(20, page.Runs.Count);
        Assert.Equal(Noon.AddMinutes(-100), page.Runs[0].StartedAt);
        Assert.Equal(Noon.AddMinutes(-119), page.Runs[^1].StartedAt);
        Assert.Equal(120, page.TotalCount);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(40)]
    public async Task GetPageAsync_ForAPagePastTheLast_ReturnsTheLastPage(int pageIndex)
    {
        await using RefreshTestHarness harness = new();
        await harness.InitializeAsync();
        await SaveRunsAsync(harness, 120);

        RunHistoryPage page = await harness.RunHistory.GetPageAsync(pageIndex);

        Assert.Equal(2, page.PageIndex);
        Assert.Equal(20, page.Runs.Count);
        Assert.Equal(120, page.TotalCount);
    }

    [Fact]
    public async Task GetPageAsync_ForANegativePage_ReturnsTheFirstPage()
    {
        await using RefreshTestHarness harness = new();
        await harness.InitializeAsync();
        await SaveRunsAsync(harness, 120);

        RunHistoryPage page = await harness.RunHistory.GetPageAsync(pageIndex: -1);

        Assert.Equal(0, page.PageIndex);
        Assert.Equal(Noon, page.Runs[0].StartedAt);
    }

    [Fact]
    public async Task GetPageAsync_WithNoRuns_ReturnsAnEmptyFirstPageAndACountOfZero()
    {
        await using RefreshTestHarness harness = new();
        await harness.InitializeAsync();

        RunHistoryPage page = await harness.RunHistory.GetPageAsync(pageIndex: 5);

        Assert.Empty(page.Runs);
        Assert.Equal(0, page.PageIndex);
        Assert.Equal(0, page.TotalCount);
    }

    private static async Task SaveRunsAsync(RefreshTestHarness harness, int count)
    {
        await using JobHunterDbContext context = await harness.ContextFactory.CreateDbContextAsync();

        for (int minutesAgo = 0; minutesAgo < count; minutesAgo++)
        {
            FetchRun run = FetchRun.Start(FetchTrigger.Startup, Noon.AddMinutes(-minutesAgo));
            run.Complete(Noon.AddMinutes(-minutesAgo).AddSeconds(30));
            context.FetchRuns.Add(run);
        }

        await context.SaveChangesAsync();
    }
}
