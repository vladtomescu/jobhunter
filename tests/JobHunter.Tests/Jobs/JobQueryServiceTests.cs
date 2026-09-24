using JobHunter.Domain;
using JobHunter.Jobs;
using JobHunter.Sources;

namespace JobHunter.Tests.Jobs;

/// <summary>Proves that the inbox shows only the jobs still waiting for a decision, in the order pay and freshness dictate, and that the full list narrows the way the filters say.</summary>
public sealed class JobQueryServiceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetInboxAsync_WithJobsInEveryState_ReturnsOnlyTheUndecidedClassAAndBJobs()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();

        Job classA = ListedJobs.NewInboxJob("Alpha", JobClass.A, 120_000m, Noon);
        Job classB = ListedJobs.NewInboxJob("Bravo", JobClass.B, 90_000m, Noon);
        Job classC = ListedJobs.NewInboxJob("Charlie", JobClass.C, 80_000m, Noon);
        Job classD = ListedJobs.NewInboxJob("Delta", JobClass.D, 70_000m, Noon);

        Job dropped = ListedJobs.NewInboxJob("Echo", JobClass.A, 110_000m, Noon);
        dropped.Drop("stale");

        Job inactive = ListedJobs.NewInboxJob("Foxtrot", JobClass.A, 110_000m, Noon);
        inactive.MissRun();
        inactive.MissRun();

        Job skipped = ListedJobs.NewInboxJob("Golf", JobClass.A, 110_000m, Noon);
        skipped.Skip(Noon);

        Job pursued = ListedJobs.NewInboxJob("Hotel", JobClass.A, 110_000m, Noon);
        pursued.Pursue(Noon);

        Job applied = ListedJobs.NewInboxJob("India", JobClass.A, 110_000m, Noon);

        await harness.SaveAsync(classA, classB, classC, classD, dropped, inactive, skipped, pursued, applied);
        await harness.SaveAsync(Application.Create(applied.Id, ApplicationStatus.Applied, Noon, "marked applied without triage"));

        IReadOnlyList<InboxRow> rows = await harness.Queries.GetInboxAsync();

        Assert.Equal<string>(["Alpha", "Bravo"], [.. rows.Select(row => row.Company)]);
    }

    [Fact]
    public async Task GetInboxAsync_WithMixedClassesAndPay_OrdersByClassThenPayThenNewest()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();

        Job classBRich = ListedJobs.NewInboxJob("Bravo rich", JobClass.B, 200_000m, Noon);
        Job classAPoor = ListedJobs.NewInboxJob("Alpha poor", JobClass.A, 80_000m, Noon);
        Job classARich = ListedJobs.NewInboxJob("Alpha rich", JobClass.A, 150_000m, Noon);
        Job classAUnknownOld = ListedJobs.NewInboxJob("Alpha unknown old", JobClass.A, null, Noon.AddDays(-3));
        Job classAUnknownNew = ListedJobs.NewInboxJob("Alpha unknown new", JobClass.A, null, Noon);

        await harness.SaveAsync(classBRich, classAPoor, classARich, classAUnknownOld, classAUnknownNew);

        IReadOnlyList<InboxRow> rows = await harness.Queries.GetInboxAsync();

        Assert.Equal<string>(["Alpha rich", "Alpha poor", "Alpha unknown new", "Alpha unknown old", "Bravo rich"], [.. rows.Select(row => row.Company)]);
    }

    [Fact]
    public async Task GetInboxAsync_ForJobsOfOneIntakeWithEqualPay_OrdersByPostingDateNewestFirst()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();

        Job postedLastWeek = ListedJobs.NewInboxJob("Alpha last week", JobClass.A, 100_000m, Noon, postedAt: Noon.AddDays(-7));
        Job postedYesterday = ListedJobs.NewInboxJob("Alpha yesterday", JobClass.A, 100_000m, Noon, postedAt: Noon.AddDays(-1));
        Job postingUnknown = ListedJobs.NewInboxJob("Alpha unknown posting", JobClass.A, 100_000m, Noon);

        await harness.SaveAsync(postedLastWeek, postedYesterday, postingUnknown);

        IReadOnlyList<InboxRow> rows = await harness.Queries.GetInboxAsync();

        Assert.Equal<string>(["Alpha unknown posting", "Alpha yesterday", "Alpha last week"], [.. rows.Select(row => row.Company)]);
    }

    [Fact]
    public async Task GetInboxAsync_ForAScoredJob_CarriesTheScoreFactsAndFlags()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();

        Job job = ListedJobs.NewInboxJob("Alpha", JobClass.A, 120_000m, Noon);
        await harness.SaveAsync(job);

        InboxRow row = Assert.Single(await harness.Queries.GetInboxAsync());

        Assert.Equal(JobClass.A, row.Class);
        Assert.Equal(12, row.Total);
        Assert.Equal("Strong platform fit.", row.Reasoning);
        Assert.Equal("senior", row.LevelGuess);
        Assert.Equal("remote", row.RemotePolicy);
        Assert.Equal(120_000m, row.CompMaxEurYear);
        Assert.Equal<JobFlag>([JobFlag.H1], row.Flags);
    }

    [Fact]
    public async Task CountJobsAwaitingScoreAsync_WithScoredAndUnscoredJobs_CountsOnlyTheActivePassedUnscoredOnes()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();

        Job scored = ListedJobs.NewInboxJob("Alpha", JobClass.A, 120_000m, Noon);

        Job unscored = ListedJobs.NewJob("Bravo", "Backend Engineer", Noon);
        unscored.ApplyPrefilterVerdict(PrefilterState.Passed, null, []);

        Job droppedUnscored = ListedJobs.NewJob("Charlie", "Backend Engineer", Noon);
        droppedUnscored.ApplyPrefilterVerdict(PrefilterState.Dropped, "title excluded", []);

        await harness.SaveAsync(scored, unscored, droppedUnscored);

        Assert.Equal(1, await harness.Queries.CountJobsAwaitingScoreAsync());
    }

    [Fact]
    public async Task GetJobsAsync_WithoutFilters_ReturnsEveryJobNewestFirstWithItsDropReason()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();

        Job kept = ListedJobs.NewInboxJob("Alpha", JobClass.A, 120_000m, Noon);

        Job dropped = ListedJobs.NewJob("Bravo", "Frontend Engineer", Noon.AddDays(-1));
        dropped.ApplyPrefilterVerdict(PrefilterState.Dropped, "title excluded: frontend", []);

        await harness.SaveAsync(kept, dropped);

        IReadOnlyList<JobListRow> rows = await harness.Queries.GetJobsAsync(new JobListFilter());

        Assert.Equal<string>(["Alpha", "Bravo"], [.. rows.Select(row => row.Company)]);
        Assert.Equal("title excluded: frontend", rows[1].DropReason);
        Assert.Equal(JobClass.D, rows[1].Class);
        Assert.Equal(12, rows[0].Total);
    }

    [Fact]
    public async Task GetJobsAsync_FilteredByClass_ReturnsOnlyThatClass()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(
            ListedJobs.NewInboxJob("Alpha", JobClass.A, 120_000m, Noon),
            ListedJobs.NewInboxJob("Bravo", JobClass.B, 90_000m, Noon));

        IReadOnlyList<JobListRow> rows = await harness.Queries.GetJobsAsync(new JobListFilter { Class = JobClass.B });

        Assert.Equal("Bravo", Assert.Single(rows).Company);
    }

    [Fact]
    public async Task GetJobsAsync_FilteredBySourceKind_ReturnsOnlyJobsCarriedByThatSource()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(
            ListedJobs.NewJob("Alpha", "Backend Engineer", Noon, JobSourceKind.RemoteOk),
            ListedJobs.NewJob("Bravo", "Backend Engineer", Noon, JobSourceKind.Dataset));

        IReadOnlyList<JobListRow> rows = await harness.Queries.GetJobsAsync(new JobListFilter { Source = JobSourceKind.Dataset });

        JobListRow row = Assert.Single(rows);
        Assert.Equal("Bravo", row.Company);
        Assert.Equal(JobSourceKind.Dataset, Assert.Single(row.Sources).Kind);
    }

    [Fact]
    public async Task GetJobsAsync_FilteredByFlag_ReturnsOnlyJobsCarryingThatFlag()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();

        Job flagged = ListedJobs.NewJob("Alpha", "Backend Engineer", Noon);
        flagged.ApplyPrefilterVerdict(PrefilterState.Passed, null, [JobFlag.H3, JobFlag.CU]);

        Job plain = ListedJobs.NewJob("Bravo", "Backend Engineer", Noon);
        plain.ApplyPrefilterVerdict(PrefilterState.Passed, null, [JobFlag.CU]);

        await harness.SaveAsync(flagged, plain);

        IReadOnlyList<JobListRow> rows = await harness.Queries.GetJobsAsync(new JobListFilter { Flag = JobFlag.H3 });

        Assert.Equal("Alpha", Assert.Single(rows).Company);
    }

    [Fact]
    public async Task GetJobsAsync_FilteredByTriageAndPrefilter_ReturnsOnlyJobsInThoseStates()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();

        Job skipped = ListedJobs.NewInboxJob("Alpha", JobClass.A, 120_000m, Noon);
        skipped.Skip(Noon);

        Job dropped = ListedJobs.NewJob("Bravo", "Frontend Engineer", Noon);
        dropped.ApplyPrefilterVerdict(PrefilterState.Dropped, "title excluded: frontend", []);

        Job waiting = ListedJobs.NewInboxJob("Charlie", JobClass.B, 90_000m, Noon);

        await harness.SaveAsync(skipped, dropped, waiting);

        Assert.Equal("Alpha", Assert.Single(await harness.Queries.GetJobsAsync(new JobListFilter { Triage = TriageState.Skipped })).Company);
        Assert.Equal("Bravo", Assert.Single(await harness.Queries.GetJobsAsync(new JobListFilter { Prefilter = PrefilterState.Dropped })).Company);
    }

    [Fact]
    public async Task GetJobsAsync_FilteredByScoring_ReturnsOnlyJobsInThatScoringState()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();

        Job scored = ListedJobs.NewInboxJob("Alpha", JobClass.A, 120_000m, Noon);

        Job failed = ListedJobs.NewJob("Bravo", "Backend Engineer", Noon);
        failed.FailScoring("Anthropic account usage limit reached — access returns 2026-10-01 00:00 UTC");

        Job unscored = ListedJobs.NewJob("Charlie", "Backend Engineer", Noon);

        await harness.SaveAsync(scored, failed, unscored);

        JobListRow failedRow = Assert.Single(await harness.Queries.GetJobsAsync(new JobListFilter { Scoring = ScoringState.Failed }));
        Assert.Equal("Bravo", failedRow.Company);
        Assert.Equal(ScoringState.Failed, failedRow.Scoring);
        Assert.Equal("Charlie", Assert.Single(await harness.Queries.GetJobsAsync(new JobListFilter { Scoring = ScoringState.Unscored })).Company);
        Assert.Equal("Alpha", Assert.Single(await harness.Queries.GetJobsAsync(new JobListFilter { Scoring = ScoringState.Scored })).Company);
    }

    [Fact]
    public async Task GetJobsAsync_FilteredByText_MatchesCompanyAndTitleWithoutCaseSensitivity()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(
            ListedJobs.NewJob("Northwind Labs", "Backend Engineer", Noon),
            ListedJobs.NewJob("Contoso", "Platform Engineer, northwind team", Noon),
            ListedJobs.NewJob("Fabrikam", "Backend Engineer", Noon));

        IReadOnlyList<JobListRow> rows = await harness.Queries.GetJobsAsync(new JobListFilter { Text = "NORTHWIND" });

        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain(rows, row => row.Company == "Fabrikam");
    }

    [Fact]
    public async Task GetJobAsync_ForAPursuedJob_ReturnsTheJobWithItsApplication()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();

        Job job = ListedJobs.NewInboxJob("Alpha", JobClass.A, 120_000m, Noon);
        await harness.SaveAsync(job);
        await harness.SaveAsync(Application.Create(job.Id, ApplicationStatus.Saved, Noon, "pursued from the inbox"));

        JobDetailView? view = await harness.Queries.GetJobAsync(job.Id);

        Assert.NotNull(view);
        Assert.Equal("Alpha", view.Job.Company);
        Assert.NotNull(view.Application);
        Assert.Equal(ApplicationStatus.Saved, view.Application.Status);
    }

    [Fact]
    public async Task GetJobAsync_ForAnUnknownIdentifier_ReturnsNull()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();

        Assert.Null(await harness.Queries.GetJobAsync(Guid.CreateVersion7()));
    }

    [Fact]
    public async Task GetDescriptionAsync_ForAStoredJob_ReturnsItsText()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();

        Job job = ListedJobs.NewJob("Alpha", "Backend Engineer", Noon);
        await harness.SaveAsync(job);

        Assert.Equal("Plain text description.", await harness.Queries.GetDescriptionAsync(job.Id));
    }
}
