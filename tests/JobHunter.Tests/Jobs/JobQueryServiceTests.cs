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
    public async Task GetInboxAsync_WithEqualScores_OrdersByClassThenNewestThenPay()
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
    public async Task GetInboxAsync_WithinAClass_OrdersByScoreThenNewestPostingBeforePay()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(
            ListedJobs.NewInboxJob("Alpha ten four days rich", JobClass.A, 200_000m, Noon, postedAt: Noon.AddDays(-4), scoreTotal: 10),
            ListedJobs.NewInboxJob("Alpha ten two days", JobClass.A, 80_000m, Noon, postedAt: Noon.AddDays(-2), scoreTotal: 10),
            ListedJobs.NewInboxJob("Bravo nine today", JobClass.B, 90_000m, Noon, postedAt: Noon, scoreTotal: 9),
            ListedJobs.NewInboxJob("Alpha eleven old", JobClass.A, 80_000m, Noon, postedAt: Noon.AddDays(-16), scoreTotal: 11),
            ListedJobs.NewInboxJob("Alpha ten three days", JobClass.A, 90_000m, Noon, postedAt: Noon.AddDays(-3), scoreTotal: 10));

        IReadOnlyList<InboxRow> rows = (await harness.Queries.GetInboxAsync(new InboxFilter())).Rows;

        Assert.Equal<string>(["Alpha eleven old", "Alpha ten two days", "Alpha ten three days", "Alpha ten four days rich", "Bravo nine today"], [.. rows.Select(row => row.Company)]);
    }

    [Fact]
    public async Task GetInboxAsync_PostedSince_KeepsTheJobsPostedOrFirstSeenFromThatMoment()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(
            ListedJobs.NewInboxJob("Alpha posted yesterday", JobClass.A, 100_000m, Noon, postedAt: Noon.AddDays(-1)),
            ListedJobs.NewInboxJob("Alpha posted last week, seen today", JobClass.A, 100_000m, Noon, postedAt: Noon.AddDays(-7)),
            ListedJobs.NewInboxJob("Bravo seen yesterday", JobClass.B, 100_000m, Noon.AddDays(-1)),
            ListedJobs.NewInboxJob("Bravo seen last week", JobClass.B, 100_000m, Noon.AddDays(-7)));

        InboxPage page = await harness.Queries.GetInboxAsync(new InboxFilter { PostedSince = Noon.AddDays(-2) });

        Assert.Equal<string>(["Alpha posted yesterday", "Bravo seen yesterday"], [.. page.Rows.Select(row => row.Company)]);
        Assert.Equal(1, page.ClassCounts[JobClass.A]);
        Assert.Equal(1, page.ClassCounts[JobClass.B]);
    }

    [Theory]
    [InlineData(InboxPayFilter.Stated, new[] { "Alpha paid" })]
    [InlineData(InboxPayFilter.NotStated, new[] { "Alpha unpaid" })]
    [InlineData(InboxPayFilter.Any, new[] { "Alpha paid", "Alpha unpaid" })]
    public async Task GetInboxAsync_FilteredByPay_KeepsTheJobsWithOrWithoutAPayFigure(InboxPayFilter pay, string[] expected)
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(
            ListedJobs.NewInboxJob("Alpha paid", JobClass.A, 100_000m, Noon),
            ListedJobs.NewInboxJob("Alpha unpaid", JobClass.A, null, Noon));

        InboxPage page = await harness.Queries.GetInboxAsync(new InboxFilter { Pay = pay });

        Assert.Equal<string>(expected, [.. page.Rows.Select(row => row.Company)]);
    }

    [Theory]
    [InlineData(InboxPlaceFilter.Remote, new[] { "Alpha remote" })]
    [InlineData(InboxPlaceFilter.OnsiteWithRelocation, new[] { "Alpha onsite relocation", "Alpha hybrid visa" })]
    [InlineData(InboxPlaceFilter.Rest, new[] { "Alpha onsite only", "Alpha unknown" })]
    [InlineData(InboxPlaceFilter.Any, new[] { "Alpha remote", "Alpha onsite relocation", "Alpha hybrid visa", "Alpha onsite only", "Alpha unknown" })]
    public async Task GetInboxAsync_FilteredByPlace_SplitsRemoteFromOnsiteWithRelocationAndTheRest(InboxPlaceFilter place, string[] expected)
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(
            ListedJobs.NewInboxJob("Alpha remote", JobClass.A, 100_000m, Noon, scoreTotal: 14, description: "We offer relocation to anyone."),
            ListedJobs.NewInboxJob("Alpha onsite relocation", JobClass.A, 100_000m, Noon, scoreTotal: 13, remotePolicy: "onsite", description: "Relocation package included."),
            ListedJobs.NewInboxJob("Alpha hybrid visa", JobClass.A, 100_000m, Noon, scoreTotal: 12, remotePolicy: "hybrid", description: "We sponsor your VISA."),
            ListedJobs.NewInboxJob("Alpha onsite only", JobClass.A, 100_000m, Noon, scoreTotal: 11, remotePolicy: "onsite", description: "Advisable to live nearby."),
            ListedJobs.NewInboxJob("Alpha unknown", JobClass.A, 100_000m, Noon, scoreTotal: 10, remotePolicy: "unknown", description: "Relocation offered."));

        InboxPage page = await harness.Queries.GetInboxAsync(new InboxFilter { Place = place });

        Assert.Equal<string>(expected, [.. page.Rows.Select(row => row.Company)]);
    }

    [Fact]
    public async Task GetInboxAsync_FilteredByPayAndPlace_KeepsTheJobsMatchingBoth()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(
            ListedJobs.NewInboxJob("Alpha remote paid", JobClass.A, 100_000m, Noon),
            ListedJobs.NewInboxJob("Alpha remote unpaid", JobClass.A, null, Noon),
            ListedJobs.NewInboxJob("Bravo onsite paid", JobClass.B, 100_000m, Noon, remotePolicy: "onsite"));

        InboxPage page = await harness.Queries.GetInboxAsync(new InboxFilter { Pay = InboxPayFilter.Stated, Place = InboxPlaceFilter.Remote });

        InboxRow row = Assert.Single(page.Rows);
        Assert.Equal("Alpha remote paid", row.Company);
        Assert.Equal(1, page.ClassCounts[JobClass.A]);
        Assert.False(page.ClassCounts.ContainsKey(JobClass.B));
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
    public async Task GetInboxAsync_FilteredByOneFlag_ReturnsOnlyJobsCarryingThatFlag()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(
            ListedJobs.NewInboxJob("Alpha", JobClass.A, 120_000m, Noon, flags: [JobFlag.H3]),
            ListedJobs.NewInboxJob("Bravo", JobClass.B, 90_000m, Noon, flags: [JobFlag.H3]),
            ListedJobs.NewInboxJob("Charlie", JobClass.A, 120_000m, Noon, flags: [JobFlag.H4]));

        InboxPage page = await harness.Queries.GetInboxAsync(new InboxFilter { Flags = [JobFlag.H3] });

        Assert.Equal<string>(["Alpha", "Bravo"], [.. page.Rows.Select(row => row.Company)]);
        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task GetInboxAsync_FilteredByTwoFlags_ReturnsOnlyJobsCarryingBoth()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(
            ListedJobs.NewInboxJob("Alpha", JobClass.A, 120_000m, Noon, flags: [JobFlag.H3, JobFlag.H4]),
            ListedJobs.NewInboxJob("Bravo", JobClass.B, 90_000m, Noon, flags: [JobFlag.H3]),
            ListedJobs.NewInboxJob("Charlie", JobClass.A, 80_000m, Noon, flags: [JobFlag.H4]),
            ListedJobs.NewInboxJob("Delta", JobClass.B, 70_000m, Noon, flags: [JobFlag.H4, JobFlag.CU, JobFlag.H3]));

        InboxPage page = await harness.Queries.GetInboxAsync(new InboxFilter { Flags = [JobFlag.H3, JobFlag.H4] });

        Assert.Equal<string>(["Alpha", "Delta"], [.. page.Rows.Select(row => row.Company)]);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(1, page.ClassCounts[JobClass.A]);
        Assert.Equal(1, page.ClassCounts[JobClass.B]);
    }

    [Fact]
    public async Task GetInboxAsync_WithNoFlagTicked_ReturnsEveryJobWaiting()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(
            ListedJobs.NewInboxJob("Alpha", JobClass.A, 120_000m, Noon, flags: [JobFlag.H3, JobFlag.H4]),
            ListedJobs.NewInboxJob("Bravo", JobClass.B, 90_000m, Noon, flags: [JobFlag.H3]),
            ListedJobs.NewInboxJob("Charlie", JobClass.B, 80_000m, Noon, flags: [JobFlag.CU]));

        InboxPage page = await harness.Queries.GetInboxAsync(new InboxFilter { Flags = [] });

        Assert.Equal(3, page.Rows.Count);
        Assert.Equal(3, page.TotalCount);
    }

    [Fact]
    public async Task GetInboxAsync_FilteredByClassAndFlag_PagesTheJobsMatchingBoth()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();

        List<Job> jobs = [.. NumberedInboxJobs(JobClass.A, 20, [JobFlag.H3]), .. NumberedInboxJobs(JobClass.B, Paging.PageSize + 5, [JobFlag.H3]), .. NumberedInboxJobs(JobClass.B, 15, [JobFlag.H4])];
        await harness.SaveAsync([.. jobs]);

        InboxFilter filter = new() { Class = JobClass.B, Flags = [JobFlag.H3], PageIndex = 1 };
        InboxPage page = await harness.Queries.GetInboxAsync(filter);

        Assert.Equal(Paging.PageSize + 5, page.TotalCount);
        Assert.Equal(5, page.Rows.Count);
        Assert.All(page.Rows, row => Assert.Equal(JobClass.B, row.Class));
        Assert.All(page.Rows, row => Assert.Contains(JobFlag.H3, row.Flags));
    }

    [Fact]
    public async Task GetInboxAsync_FilteredByClassAndTwoFlags_PagesTheJobsMatchingAll()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();

        List<Job> jobs =
        [
            .. NumberedInboxJobs(JobClass.A, 20, [JobFlag.H3, JobFlag.H4]),
            .. NumberedInboxJobs(JobClass.B, Paging.PageSize + 5, [JobFlag.H3, JobFlag.H4]),
            .. NumberedInboxJobs(JobClass.B, 15, [JobFlag.H3]),
            .. NumberedInboxJobs(JobClass.B, 15, [JobFlag.H4]),
        ];
        await harness.SaveAsync([.. jobs]);

        InboxFilter filter = new() { Class = JobClass.B, Flags = [JobFlag.H3, JobFlag.H4] };
        InboxPage first = await harness.Queries.GetInboxAsync(filter);
        filter.PageIndex = 1;
        InboxPage second = await harness.Queries.GetInboxAsync(filter);

        Assert.Equal(Paging.PageSize + 5, first.TotalCount);
        Assert.Equal(Paging.PageSize + 5, first.ClassCounts[JobClass.B]);
        Assert.False(first.ClassCounts.ContainsKey(JobClass.A));
        Assert.Equal(Paging.PageSize, first.Rows.Count);
        Assert.Equal(5, second.Rows.Count);
        Assert.Equal(Paging.PageSize + 5, first.Rows.Concat(second.Rows).Select(row => row.Id).Distinct().Count());
        Assert.All(first.Rows.Concat(second.Rows), row => Assert.Equal(JobClass.B, row.Class));
        Assert.All(first.Rows.Concat(second.Rows), row => Assert.Contains(JobFlag.H3, row.Flags));
        Assert.All(first.Rows.Concat(second.Rows), row => Assert.Contains(JobFlag.H4, row.Flags));
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
    public async Task GetJobsAsync_FilteredByOneFlag_ReturnsOnlyJobsCarryingThatFlag()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();

        Job flagged = ListedJobs.NewJob("Alpha", "Backend Engineer", Noon);
        flagged.ApplyPrefilterVerdict(PrefilterState.Passed, null, [JobFlag.H3, JobFlag.CU]);

        Job plain = ListedJobs.NewJob("Bravo", "Backend Engineer", Noon);
        plain.ApplyPrefilterVerdict(PrefilterState.Passed, null, [JobFlag.CU]);

        await harness.SaveAsync(flagged, plain);

        IReadOnlyList<JobListRow> rows = (await harness.Queries.GetJobsAsync(new JobListFilter { Flags = [JobFlag.H3] })).Rows;

        Assert.Equal("Alpha", Assert.Single(rows).Company);
    }

    [Fact]
    public async Task GetJobsAsync_FilteredByTwoFlags_ReturnsOnlyJobsCarryingBoth()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(
            FlaggedJob("Alpha", [JobFlag.H3, JobFlag.CU]),
            FlaggedJob("Bravo", [JobFlag.CU]),
            FlaggedJob("Charlie", [JobFlag.H3]),
            FlaggedJob("Delta", [JobFlag.H4, JobFlag.CU, JobFlag.H3]));

        JobListPage page = await harness.Queries.GetJobsAsync(new JobListFilter { Flags = [JobFlag.H3, JobFlag.CU] });

        Assert.Equal<string>(["Alpha", "Delta"], [.. page.Rows.Select(row => row.Company).Order()]);
        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task GetJobsAsync_WithNoFlagTicked_ReturnsEveryJob()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(
            FlaggedJob("Alpha", [JobFlag.H3, JobFlag.CU]),
            FlaggedJob("Bravo", [JobFlag.CU]),
            FlaggedJob("Charlie", []));

        JobListPage page = await harness.Queries.GetJobsAsync(new JobListFilter { Flags = [] });

        Assert.Equal(3, page.Rows.Count);
        Assert.Equal(3, page.TotalCount);
    }

    [Fact]
    public async Task GetJobsAsync_FilteredByTwoFlagsAndPaged_CountsAndPagesOnlyTheJobsCarryingBoth()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();

        List<Job> jobs = [];
        for (int index = 0; index < Paging.PageSize + 5; index++)
        {
            jobs.Add(FlaggedJob($"Both {index:D3}", [JobFlag.H3, JobFlag.CU], Noon.AddMinutes(-index)));
        }

        for (int index = 0; index < 20; index++)
        {
            jobs.Add(FlaggedJob($"First only {index:D3}", [JobFlag.H3], Noon.AddMinutes(-index)));
            jobs.Add(FlaggedJob($"Second only {index:D3}", [JobFlag.CU], Noon.AddMinutes(-index)));
        }

        await harness.SaveAsync([.. jobs]);

        JobListFilter filter = new() { Flags = [JobFlag.H3, JobFlag.CU] };
        JobListPage first = await harness.Queries.GetJobsAsync(filter);
        filter.PageIndex = 1;
        JobListPage second = await harness.Queries.GetJobsAsync(filter);

        Assert.Equal(Paging.PageSize + 5, first.TotalCount);
        Assert.Equal(Paging.PageSize, first.Rows.Count);
        Assert.Equal(5, second.Rows.Count);
        Assert.Equal(Paging.PageSize + 5, first.Rows.Concat(second.Rows).Select(row => row.Id).Distinct().Count());
        Assert.All(first.Rows.Concat(second.Rows), row => Assert.StartsWith("Both ", row.Company, StringComparison.Ordinal));
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
    public async Task GetJobsAsync_OrderedByAge_SortsByPostingDateAndFallsBackToFirstSeen()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(
            ListedJobs.NewInboxJob("Alpha posted last week, seen today", JobClass.A, 100_000m, Noon, postedAt: Noon.AddDays(-7)),
            ListedJobs.NewInboxJob("Bravo posted yesterday, seen today", JobClass.B, 100_000m, Noon, postedAt: Noon.AddDays(-1)),
            ListedJobs.NewJob("Charlie seen three days ago", "Backend Engineer", Noon.AddDays(-3)));

        JobListPage newestFirst = await harness.Queries.GetJobsAsync(new JobListFilter());

        Assert.Equal<string>(["Bravo posted yesterday, seen today", "Charlie seen three days ago", "Alpha posted last week, seen today"], [.. newestFirst.Rows.Select(row => row.Company)]);
    }

    [Fact]
    public async Task GetJobsAsync_PostedSince_KeepsTheJobsPostedOrFirstSeenFromThatMoment()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(
            ListedJobs.NewInboxJob("Alpha posted yesterday", JobClass.A, 100_000m, Noon, postedAt: Noon.AddDays(-1)),
            ListedJobs.NewInboxJob("Bravo posted last week, seen today", JobClass.B, 100_000m, Noon, postedAt: Noon.AddDays(-7)),
            ListedJobs.NewJob("Charlie seen yesterday", "Backend Engineer", Noon.AddDays(-1)),
            ListedJobs.NewJob("Delta seen last week", "Backend Engineer", Noon.AddDays(-7)));

        JobListPage page = await harness.Queries.GetJobsAsync(new JobListFilter { PostedSince = Noon.AddDays(-2) });

        Assert.Equal<string>(["Alpha posted yesterday", "Charlie seen yesterday"], [.. page.Rows.Select(row => row.Company).Order()]);
        Assert.Equal(2, page.TotalCount);
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
    private static Job[] NumberedInboxJobs(JobClass jobClass, int count, IReadOnlyCollection<JobFlag>? flags = null)
    {
        return [.. Enumerable.Range(0, count).Select(index => ListedJobs.NewInboxJob($"{jobClass} {index:D3}", jobClass, 100_000m, Noon.AddMinutes(-index), flags: flags))];
    }

    /// <summary>A job that passed the rules carrying exactly the flags, first seen at the given time.</summary>
    private static Job FlaggedJob(string company, IReadOnlyCollection<JobFlag> flags, DateTimeOffset? firstSeenAt = null)
    {
        Job job = ListedJobs.NewJob(company, "Backend Engineer", firstSeenAt ?? Noon);
        job.ApplyPrefilterVerdict(PrefilterState.Passed, null, flags);

        return job;
    }

    /// <summary>Jobs named Company 000, 001 and so on, each seen one minute before the one named before it, so that the newest-first order is the numbering.</summary>
    private static Job[] NumberedJobs(int count)
    {
        return [.. Enumerable.Range(0, count).Select(index => ListedJobs.NewJob($"Company {index:D3}", "Backend Engineer", Noon.AddMinutes(-index)))];
    }
}
