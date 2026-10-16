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

        IReadOnlyList<InboxRow> rows = (await harness.Queries.GetInboxAsync(new InboxFilter())).Rows;

        Assert.Equal<string>(["Alpha", "Bravo"], [.. rows.Select(row => row.Company)]);
    }

    [Fact]
    public async Task GetInboxAsync_WithEqualScores_OrdersByClassThenPayThenNewest()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();

        Job classBRich = ListedJobs.NewInboxJob("Bravo rich", JobClass.B, 200_000m, Noon);
        Job classAPoor = ListedJobs.NewInboxJob("Alpha poor", JobClass.A, 80_000m, Noon);
        Job classARich = ListedJobs.NewInboxJob("Alpha rich", JobClass.A, 150_000m, Noon);
        Job classAUnknownOld = ListedJobs.NewInboxJob("Alpha unknown old", JobClass.A, null, Noon.AddDays(-3));
        Job classAUnknownNew = ListedJobs.NewInboxJob("Alpha unknown new", JobClass.A, null, Noon);

        await harness.SaveAsync(classBRich, classAPoor, classARich, classAUnknownOld, classAUnknownNew);

        IReadOnlyList<InboxRow> rows = (await harness.Queries.GetInboxAsync(new InboxFilter())).Rows;

        Assert.Equal<string>(["Alpha rich", "Alpha poor", "Alpha unknown new", "Alpha unknown old", "Bravo rich"], [.. rows.Select(row => row.Company)]);
    }

    [Fact]
    public async Task GetInboxAsync_WithMixedClassesAndScores_OrdersByClassThenHighestScoreBeforePay()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();

        Job classBTop = ListedJobs.NewInboxJob("Bravo top", JobClass.B, 90_000m, Noon, scoreTotal: 13);
        Job classALowRich = ListedJobs.NewInboxJob("Alpha low rich", JobClass.A, 200_000m, Noon, scoreTotal: 11);
        Job classAHighPoor = ListedJobs.NewInboxJob("Alpha high poor", JobClass.A, 80_000m, Noon, scoreTotal: 13);
        Job classBLow = ListedJobs.NewInboxJob("Bravo low", JobClass.B, 150_000m, Noon, scoreTotal: 9);

        await harness.SaveAsync(classBTop, classALowRich, classAHighPoor, classBLow);

        IReadOnlyList<InboxRow> rows = (await harness.Queries.GetInboxAsync(new InboxFilter())).Rows;

        Assert.Equal<string>(["Alpha high poor", "Alpha low rich", "Bravo top", "Bravo low"], [.. rows.Select(row => row.Company)]);
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

        IReadOnlyList<InboxRow> rows = (await harness.Queries.GetInboxAsync(new InboxFilter())).Rows;

        Assert.Equal<string>(["Alpha unknown posting", "Alpha yesterday", "Alpha last week"], [.. rows.Select(row => row.Company)]);
    }

    [Fact]
    public async Task GetInboxAsync_ForAScoredJob_CarriesTheScoreDimensionsFactsAndFlags()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();

        Job job = ListedJobs.NewInboxJob("Alpha", JobClass.A, 120_000m, Noon);
        await harness.SaveAsync(job);

        InboxRow row = Assert.Single((await harness.Queries.GetInboxAsync(new InboxFilter())).Rows);

        Assert.Equal(JobClass.A, row.Class);
        Assert.Equal(12, row.Total);
        Assert.Equal(new ScorePoints(2, 2, 2, 1, 2, 2, 1), row.Points);
        Assert.Equal("Strong platform fit.", row.Reasoning);
        Assert.Equal("senior", row.LevelGuess);
        Assert.Equal("remote", row.RemotePolicy);
        Assert.Equal(120_000m, row.CompMaxPerYear);
        Assert.Equal<JobFlag>([JobFlag.H1, JobFlag.HighPay], row.Flags);
    }

    [Fact]
    public async Task GetInboxAsync_WithMoreJobsThanOnePage_ReturnsTheFirstPageWithBothClassesAndTheirCounts()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync([.. NumberedInboxJobs(JobClass.A, 30), .. NumberedInboxJobs(JobClass.B, 40)]);

        InboxPage page = await harness.Queries.GetInboxAsync(new InboxFilter());

        Assert.Equal(Paging.PageSize, page.Rows.Count);
        Assert.Equal(70, page.TotalCount);
        Assert.Equal(0, page.PageIndex);
        Assert.Equal(30, page.ClassCounts[JobClass.A]);
        Assert.Equal(40, page.ClassCounts[JobClass.B]);
        Assert.All(page.Rows.Take(30), row => Assert.Equal(JobClass.A, row.Class));
        Assert.All(page.Rows.Skip(30), row => Assert.Equal(JobClass.B, row.Class));
        Assert.Equal("A 000", page.Rows[0].Company);
        Assert.Equal("B 019", page.Rows[^1].Company);
    }

    [Fact]
    public async Task GetInboxAsync_ForTheSecondPage_ContinuesTheOrderWhereTheFirstPageStopped()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync([.. NumberedInboxJobs(JobClass.A, 30), .. NumberedInboxJobs(JobClass.B, 40)]);

        InboxPage page = await harness.Queries.GetInboxAsync(new InboxFilter { PageIndex = 1 });

        Assert.Equal(20, page.Rows.Count);
        Assert.Equal(1, page.PageIndex);
        Assert.Equal(70, page.TotalCount);
        Assert.Equal("B 020", page.Rows[0].Company);
        Assert.Equal("B 039", page.Rows[^1].Company);
        Assert.All(page.Rows, row => Assert.Equal(JobClass.B, row.Class));
    }

    [Fact]
    public async Task GetInboxAsync_FilteredByClass_PagesAndCountsOnlyThatClass()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync([.. NumberedInboxJobs(JobClass.A, 30), .. NumberedInboxJobs(JobClass.B, 60)]);

        InboxPage first = await harness.Queries.GetInboxAsync(new InboxFilter { Class = JobClass.B });
        InboxPage second = await harness.Queries.GetInboxAsync(new InboxFilter { Class = JobClass.B, PageIndex = 1 });

        Assert.Equal(60, first.TotalCount);
        Assert.Equal(60, first.ClassCounts[JobClass.B]);
        Assert.False(first.ClassCounts.ContainsKey(JobClass.A));
        Assert.Equal(Paging.PageSize, first.Rows.Count);
        Assert.Equal(10, second.Rows.Count);
        Assert.All(first.Rows.Concat(second.Rows), row => Assert.Equal(JobClass.B, row.Class));
    }

    [Fact]
    public async Task GetInboxAsync_FilteredByFlag_ReturnsOnlyJobsCarryingThatFlag()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(
            ListedJobs.NewInboxJob("Alpha", JobClass.A, 120_000m, Noon, flag: JobFlag.H3),
            ListedJobs.NewInboxJob("Bravo", JobClass.B, 90_000m, Noon, flag: JobFlag.H3),
            ListedJobs.NewInboxJob("Charlie", JobClass.A, 120_000m, Noon, flag: JobFlag.H4));

        InboxPage page = await harness.Queries.GetInboxAsync(new InboxFilter { Flag = JobFlag.H3 });

        Assert.Equal<string>(["Alpha", "Bravo"], [.. page.Rows.Select(row => row.Company)]);
        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task GetInboxAsync_FilteredByClassAndFlag_PagesTheJobsMatchingBoth()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();

        List<Job> jobs = [.. NumberedInboxJobs(JobClass.A, 20, JobFlag.H3), .. NumberedInboxJobs(JobClass.B, Paging.PageSize + 5, JobFlag.H3), .. NumberedInboxJobs(JobClass.B, 15, JobFlag.H4)];
        await harness.SaveAsync([.. jobs]);

        InboxFilter filter = new() { Class = JobClass.B, Flag = JobFlag.H3, PageIndex = 1 };
        InboxPage page = await harness.Queries.GetInboxAsync(filter);

        Assert.Equal(Paging.PageSize + 5, page.TotalCount);
        Assert.Equal(5, page.Rows.Count);
        Assert.All(page.Rows, row => Assert.Equal(JobClass.B, row.Class));
        Assert.All(page.Rows, row => Assert.Contains(JobFlag.H3, row.Flags));
    }

    [Fact]
    public async Task GetInboxAsync_BeyondTheLastPage_ReturnsTheLastPageWithRows()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync([.. NumberedInboxJobs(JobClass.A, Paging.PageSize + 1)]);

        InboxPage page = await harness.Queries.GetInboxAsync(new InboxFilter { PageIndex = 5 });

        Assert.Equal(1, page.PageIndex);
        Assert.Equal("A 050", Assert.Single(page.Rows).Company);
    }

    [Fact]
    public async Task GetInboxAsync_WithNothingWaiting_ReturnsAnEmptyFirstPage()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();

        InboxPage page = await harness.Queries.GetInboxAsync(new InboxFilter { PageIndex = 3 });

        Assert.Empty(page.Rows);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(0, page.PageIndex);
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

        IReadOnlyList<JobListRow> rows = (await harness.Queries.GetJobsAsync(new JobListFilter())).Rows;

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

        IReadOnlyList<JobListRow> rows = (await harness.Queries.GetJobsAsync(new JobListFilter { Class = JobClass.B })).Rows;

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

        IReadOnlyList<JobListRow> rows = (await harness.Queries.GetJobsAsync(new JobListFilter { Source = JobSourceKind.Dataset })).Rows;

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

        IReadOnlyList<JobListRow> rows = (await harness.Queries.GetJobsAsync(new JobListFilter { Flag = JobFlag.H3 })).Rows;

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

        Assert.Equal("Alpha", Assert.Single((await harness.Queries.GetJobsAsync(new JobListFilter { Triage = TriageState.Skipped })).Rows).Company);
        Assert.Equal("Bravo", Assert.Single((await harness.Queries.GetJobsAsync(new JobListFilter { Prefilter = PrefilterState.Dropped })).Rows).Company);
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

        JobListRow failedRow = Assert.Single((await harness.Queries.GetJobsAsync(new JobListFilter { Scoring = ScoringState.Failed })).Rows);
        Assert.Equal("Bravo", failedRow.Company);
        Assert.Equal(ScoringState.Failed, failedRow.Scoring);
        Assert.Equal("Charlie", Assert.Single((await harness.Queries.GetJobsAsync(new JobListFilter { Scoring = ScoringState.Unscored })).Rows).Company);
        Assert.Equal("Alpha", Assert.Single((await harness.Queries.GetJobsAsync(new JobListFilter { Scoring = ScoringState.Scored })).Rows).Company);
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

        IReadOnlyList<JobListRow> rows = (await harness.Queries.GetJobsAsync(new JobListFilter { Text = "NORTHWIND" })).Rows;

        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain(rows, row => row.Company == "Fabrikam");
    }

    [Fact]
    public async Task GetJobsAsync_WithMoreJobsThanOnePage_ReturnsTheFirstPageNewestFirstAndTheTotal()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(NumberedJobs(Paging.PageSize + 20));

        JobListPage page = await harness.Queries.GetJobsAsync(new JobListFilter());

        Assert.Equal(Paging.PageSize, page.Rows.Count);
        Assert.Equal(Paging.PageSize + 20, page.TotalCount);
        Assert.Equal(0, page.PageIndex);
        Assert.Equal("Company 000", page.Rows[0].Company);
        Assert.Equal("Company 049", page.Rows[^1].Company);
    }

    [Fact]
    public async Task GetJobsAsync_ForTheSecondPage_ReturnsTheRemainingJobsAfterTheFirstPage()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(NumberedJobs(Paging.PageSize + 20));

        JobListPage page = await harness.Queries.GetJobsAsync(new JobListFilter { PageIndex = 1 });

        Assert.Equal(20, page.Rows.Count);
        Assert.Equal(Paging.PageSize + 20, page.TotalCount);
        Assert.Equal(1, page.PageIndex);
        Assert.Equal("Company 050", page.Rows[0].Company);
        Assert.Equal("Company 069", page.Rows[^1].Company);
    }

    [Fact]
    public async Task GetJobsAsync_FilteredAndPaged_CountsAndPagesOnlyTheMatchingJobs()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();

        List<Job> jobs = [];
        for (int index = 0; index < Paging.PageSize + 10; index++)
        {
            jobs.Add(ListedJobs.NewInboxJob($"Bravo {index:D3}", JobClass.B, 90_000m, Noon.AddMinutes(-index)));
        }

        for (int index = 0; index < 30; index++)
        {
            jobs.Add(ListedJobs.NewInboxJob($"Alpha {index:D3}", JobClass.A, 120_000m, Noon.AddMinutes(-index)));
        }

        await harness.SaveAsync([.. jobs]);

        JobListPage first = await harness.Queries.GetJobsAsync(new JobListFilter { Class = JobClass.B });
        JobListPage second = await harness.Queries.GetJobsAsync(new JobListFilter { Class = JobClass.B, PageIndex = 1 });

        Assert.Equal(Paging.PageSize + 10, first.TotalCount);
        Assert.Equal(Paging.PageSize, first.Rows.Count);
        Assert.Equal(10, second.Rows.Count);
        Assert.All(first.Rows.Concat(second.Rows), row => Assert.Equal(JobClass.B, row.Class));
        Assert.Equal(Paging.PageSize + 10, first.Rows.Concat(second.Rows).Select(row => row.Id).Distinct().Count());
    }

    [Fact]
    public async Task GetJobsAsync_BeyondTheLastPage_ReturnsTheLastPage()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(NumberedJobs(Paging.PageSize + 20));

        JobListPage page = await harness.Queries.GetJobsAsync(new JobListFilter { PageIndex = 9 });

        Assert.Equal(1, page.PageIndex);
        Assert.Equal(20, page.Rows.Count);
    }

    [Fact]
    public async Task GetJobsAsync_WithNoMatch_ReturnsAnEmptyFirstPage()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(ListedJobs.NewJob("Alpha", "Backend Engineer", Noon));

        JobListPage page = await harness.Queries.GetJobsAsync(new JobListFilter { Text = "no such job", PageIndex = 3 });

        Assert.Empty(page.Rows);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(0, page.PageIndex);
    }

    [Fact]
    public async Task GetJobsAsync_OrderedByCompany_SortsAcrossPagesWithoutCaseSensitivity()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();

        List<Job> jobs = [.. NumberedJobs(Paging.PageSize - 1)];
        jobs.Add(ListedJobs.NewJob("apple", "Backend Engineer", Noon.AddDays(-1)));
        jobs.Add(ListedJobs.NewJob("Zeta", "Backend Engineer", Noon.AddDays(-2)));
        await harness.SaveAsync([.. jobs]);

        JobListPage ascendingFirst = await harness.Queries.GetJobsAsync(new JobListFilter { OrderBy = JobListColumn.Company, Descending = false });
        JobListPage ascendingSecond = await harness.Queries.GetJobsAsync(new JobListFilter { OrderBy = JobListColumn.Company, Descending = false, PageIndex = 1 });
        JobListPage descendingFirst = await harness.Queries.GetJobsAsync(new JobListFilter { OrderBy = JobListColumn.Company, Descending = true });

        Assert.Equal("apple", ascendingFirst.Rows[0].Company);
        Assert.Equal("Zeta", Assert.Single(ascendingSecond.Rows).Company);
        Assert.Equal("Zeta", descendingFirst.Rows[0].Company);
    }

    [Fact]
    public async Task GetJobsAsync_OrderedByTitleOrAge_FollowsTheChosenDirection()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(
            ListedJobs.NewJob("Alpha", "Zebra Engineer", Noon.AddDays(-1)),
            ListedJobs.NewJob("Bravo", "apple Engineer", Noon.AddDays(-2)),
            ListedJobs.NewJob("Charlie", "Mango Engineer", Noon));

        JobListPage byTitle = await harness.Queries.GetJobsAsync(new JobListFilter { OrderBy = JobListColumn.Title, Descending = false });
        JobListPage oldestFirst = await harness.Queries.GetJobsAsync(new JobListFilter { OrderBy = JobListColumn.Age, Descending = false });

        Assert.Equal<string>(["apple Engineer", "Mango Engineer", "Zebra Engineer"], [.. byTitle.Rows.Select(row => row.Title)]);
        Assert.Equal<string>(["Bravo", "Alpha", "Charlie"], [.. oldestFirst.Rows.Select(row => row.Company)]);
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

    /// <summary>Inbox jobs of one class named after it, A 000, A 001 and so on, each seen one minute before the one named before it, so that the inbox order inside the class is the numbering.</summary>
    private static Job[] NumberedInboxJobs(JobClass jobClass, int count, JobFlag flag = JobFlag.H1)
    {
        return [.. Enumerable.Range(0, count).Select(index => ListedJobs.NewInboxJob($"{jobClass} {index:D3}", jobClass, 100_000m, Noon.AddMinutes(-index), flag: flag))];
    }

    /// <summary>Jobs named Company 000, 001 and so on, each seen one minute before the one named before it, so that the newest-first order is the numbering.</summary>
    private static Job[] NumberedJobs(int count)
    {
        return [.. Enumerable.Range(0, count).Select(index => ListedJobs.NewJob($"Company {index:D3}", "Backend Engineer", Noon.AddMinutes(-index)))];
    }
}
