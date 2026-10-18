using JobHunter.Domain;
using JobHunter.Jobs;

namespace JobHunter.Tests.Jobs;

/// <summary>Proves that the skills page counts each scored job once per canonical skill under the strongest importance it gave it, reports the share and the median pay, lists every skill of the catalog, and narrows the counted jobs the way the filters say.</summary>
public sealed class SkillQueryServiceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly JobsTestHarness harness = new();

    public Task InitializeAsync()
    {
        return harness.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        return harness.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task GetSkillsAsync_WhenJobsNameASkillUnderSeveralNames_CountsEachJobOnceUnderItsStrongestImportance()
    {
        await harness.SaveAsync(
            ScoredJob(skills: [new ScoreSkill("k8s", "primary", "tool", "yes"), new ScoreSkill("Kubernetes", "nice_to_have", "tool", "yes")]),
            ScoredJob(skills: [new ScoreSkill("kubernetes", "required", "tool", "yes")]),
            ScoredJob(skills: [new ScoreSkill("golang", "nice_to_have", "language", "no")]));

        SkillList list = await harness.SkillQueries.GetSkillsAsync(new SkillListFilter(), Now);

        SkillRow kubernetes = Row(list, "Kubernetes");
        Assert.Equal(3, list.JobsWithSkills);
        Assert.Equal(2, kubernetes.JobCount);
        Assert.Equal(2.0 / 3, kubernetes.Share, 6);
        Assert.Equal((1, 1, 0), (kubernetes.PrimaryCount, kubernetes.RequiredCount, kubernetes.NiceToHaveCount));

        SkillRow go = Row(list, "Go");
        Assert.Equal(1, go.JobCount);
        Assert.Equal((0, 0, 1), (go.PrimaryCount, go.RequiredCount, go.NiceToHaveCount));
    }

    [Fact]
    public async Task GetSkillsAsync_ForJobsOfWhichSomeStatePay_ReportsTheMedianOfTheOnesThatDo()
    {
        Job minimumOnly = ScoredJob(skills: [new ScoreSkill("Go", "primary", "language", "yes")]);
        minimumOnly.RecordCompensation(150_000m, null, "EUR", CompPeriod.Year, 150_000m, null);

        await harness.SaveAsync(
            ScoredJob(80_000m, skills: [new ScoreSkill("Go", "primary", "language", "yes")]),
            ScoredJob(100_000m, skills: [new ScoreSkill("Go", "primary", "language", "yes")]),
            minimumOnly,
            ScoredJob(skills: [new ScoreSkill("Go", "primary", "language", "yes")]),
            ScoredJob(90_000m, skills: [new ScoreSkill("AWS", "required", "cloud", "yes")]),
            ScoredJob(110_000m, skills: [new ScoreSkill("AWS", "required", "cloud", "yes")]));

        SkillList list = await harness.SkillQueries.GetSkillsAsync(new SkillListFilter(), Now);

        Assert.Equal(100_000m, Row(list, "Go").MedianPayPerYear);
        Assert.Equal(100_000m, Row(list, "AWS").MedianPayPerYear);
        Assert.Null(Row(list, "Kubernetes").MedianPayPerYear);
    }

    [Fact]
    public async Task GetSkillsAsync_WithUnscoredJobsAndCardsNamingNoSkill_ListsEverySkillTheMostAskedForFirst()
    {
        Job unscored = ListedJobs.NewJob("Acme", "Backend Engineer", Now);
        await harness.SaveAsync(
            unscored,
            ScoredJob(),
            ScoredJob(skills: [new ScoreSkill("postgres", "required", "database", "yes")]),
            ScoredJob(skills: [new ScoreSkill("Postgres", "required", "database", "yes"), new ScoreSkill("js", "required", "language", "yes")]));

        SkillList list = await harness.SkillQueries.GetSkillsAsync(new SkillListFilter(), Now);

        Assert.Equal(2, list.JobsWithSkills);
        Assert.Equal<string>(["PostgreSQL", "JavaScript", ".NET", "AWS", "Go", "Kubernetes", "TypeScript"], [.. list.Rows.Select(row => row.Name)]);
        Assert.Equal(1.0, Row(list, "PostgreSQL").Share);
        Assert.Equal(0, Row(list, "Go").JobCount);
    }

    [Fact]
    public async Task GetSkillsAsync_WithAClass_CountsOnlyTheJobsOfThatClass()
    {
        await harness.SaveAsync(
            ScoredJob(jobClass: JobClass.A, skills: [new ScoreSkill("Go", "primary", "language", "yes")]),
            ScoredJob(jobClass: JobClass.C, skills: [new ScoreSkill("Go", "primary", "language", "yes")]));

        SkillList list = await harness.SkillQueries.GetSkillsAsync(new SkillListFilter { Class = JobClass.C }, Now);

        Assert.Equal(1, list.JobsWithSkills);
        Assert.Equal(1, Row(list, "Go").JobCount);
    }

    [Fact]
    public async Task GetSkillsAsync_WithAPayFloor_LeavesOutTheJobsPayingLessAndTheOnesOfUnknownPay()
    {
        await harness.SaveAsync(
            ScoredJob(120_000m, skills: [new ScoreSkill("Go", "primary", "language", "yes")]),
            ScoredJob(90_000m, skills: [new ScoreSkill("Go", "primary", "language", "yes")]),
            ScoredJob(skills: [new ScoreSkill("Go", "primary", "language", "yes")]));

        SkillList list = await harness.SkillQueries.GetSkillsAsync(new SkillListFilter { PayFloorPerYear = 100_000m }, Now);

        Assert.Equal(1, Row(list, "Go").JobCount);
        Assert.Equal(120_000m, Row(list, "Go").MedianPayPerYear);
    }

    [Fact]
    public async Task GetSkillsAsync_WithPostedWithinDays_CountsTheJobsPostedOrFirstSeenInsideTheWindow()
    {
        await harness.SaveAsync(
            ScoredJob(firstSeenAt: Now.AddDays(-3), skills: [new ScoreSkill("Go", "primary", "language", "yes")]),
            ScoredJob(firstSeenAt: Now.AddDays(-3), postedAt: Now.AddDays(-40), skills: [new ScoreSkill("Go", "primary", "language", "yes")]),
            ScoredJob(firstSeenAt: Now.AddDays(-40), skills: [new ScoreSkill("Go", "primary", "language", "yes")]));

        SkillList list = await harness.SkillQueries.GetSkillsAsync(new SkillListFilter { PostedWithinDays = 30 }, Now);

        Assert.Equal(1, Row(list, "Go").JobCount);
    }

    [Fact]
    public async Task GetSkillsAsync_ExcludingTheUnitedStates_LeavesOutTheJobsLocatedThereAndTheOnesRequiringItsAuthorization()
    {
        Job located = ScoredJob(skills: [new ScoreSkill("Go", "primary", "language", "yes")]);
        located.RecordPlace("New York", "US", null, null, null);
        Job european = ScoredJob(skills: [new ScoreSkill("Go", "primary", "language", "yes")]);
        european.RecordPlace("Berlin", "DE", null, null, null);

        await harness.SaveAsync(
            located,
            european,
            ScoredJob(flags: [JobFlag.H1, JobFlag.WA], skills: [new ScoreSkill("Go", "primary", "language", "yes")]),
            ScoredJob(skills: [new ScoreSkill("Go", "primary", "language", "yes")]));

        SkillList list = await harness.SkillQueries.GetSkillsAsync(new SkillListFilter { ExcludeUnitedStates = true }, Now);

        Assert.Equal(2, list.JobsWithSkills);
        Assert.Equal(2, Row(list, "Go").JobCount);
    }

    private static Job ScoredJob(decimal? compMaxEurYear = null, JobClass jobClass = JobClass.A, DateTimeOffset? firstSeenAt = null, DateTimeOffset? postedAt = null, IReadOnlyCollection<JobFlag>? flags = null, ScoreSkill[]? skills = null)
    {
        Job job = ListedJobs.NewInboxJob("Acme", jobClass, compMaxEurYear, firstSeenAt ?? Now, postedAt: postedAt, flags: flags);
        job.RecordScore(job.Score! with { Skills = [.. skills ?? []] }, jobClass);

        return job;
    }

    private static SkillRow Row(SkillList list, string name)
    {
        return list.Rows.Single(row => row.Name == name);
    }
}
