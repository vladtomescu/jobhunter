using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Jobs;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Tests.Jobs;

/// <summary>Proves that a CV mark set by hand sticks to the skill, that a merge hands the merged skill's names to the target, and that both reclassify the jobs whose cards name the skill under any of its names, from the stored cards alone and one catalog write at a time.</summary>
public sealed class SkillServiceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset SeenAt = new(2026, 10, 18, 8, 0, 0, TimeSpan.Zero);

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
    public async Task SetCvMarkAsync_WhenThePrimarySkillOfAClassAJobIsMarkedNo_FlipsTheJobToClassC()
    {
        Job job = ScoredJob(JobClass.A, new ScoreSkill("k8s", "primary", "tool", "yes"));
        await harness.SaveAsync(job);
        int kubernetesId = await GetSkillIdAsync("Kubernetes");

        int classesChanged = await harness.Skills.SetCvMarkAsync(kubernetesId, CvMark.No);

        Job reclassified = await harness.GetJobAsync(job.Id);
        Assert.Equal(1, classesChanged);
        Assert.Equal(JobClass.C, reclassified.Class);
        Assert.Equal(0, reclassified.Score!.Stack);
        Assert.Equal<ScoreSkill>([new ScoreSkill("k8s", "primary", "tool", "yes")], reclassified.Score.Skills);

        Skill kubernetes = await GetSkillAsync(kubernetesId);
        Assert.Equal(CvMark.No, kubernetes.CvMark);
        Assert.Equal(CvMarkSetBy.Candidate, kubernetes.CvMarkSetBy);
    }

    [Fact]
    public async Task SetCvMarkAsync_ForASkillAJobDoesNotName_LeavesThatJobsClassAlone()
    {
        Job job = ScoredJob(JobClass.B, new ScoreSkill("golang", "primary", "language", "yes"));
        await harness.SaveAsync(job);

        int classesChanged = await harness.Skills.SetCvMarkAsync(await GetSkillIdAsync("Kubernetes"), CvMark.No);

        Assert.Equal(0, classesChanged);
        Assert.Equal(JobClass.B, (await harness.GetJobAsync(job.Id)).Class);
    }

    [Fact]
    public async Task SetCvMarkAsync_WhenTheJobsOtherPrimarySkillIsNotJudgedYet_CountsThatSkillAsCovered()
    {
        Job job = ScoredJob(JobClass.A, new ScoreSkill("dotnet", "primary", "framework", "yes"), new ScoreSkill("Postgres", "primary", "database", "yes"));
        // Stored at stack 0 and class C, so only the reclassify can bring the job to stack 2 and class A.
        job.RecordScore(job.Score! with { Stack = 0 }, JobClass.C);
        await harness.SaveAsync(job);

        await harness.Skills.SetCvMarkAsync(await GetSkillIdAsync("PostgreSQL"), CvMark.Close);

        Job reclassified = await harness.GetJobAsync(job.Id);
        Assert.Null((await GetSkillAsync(await GetSkillIdAsync(".NET"))).CvMark);
        Assert.Equal(2, reclassified.Score!.Stack);
        Assert.Equal(JobClass.A, reclassified.Class);
    }

    [Fact]
    public async Task SetCvMarkAsync_WhileAnotherCatalogWriteHoldsTheGate_WaitsForItBeforeMarking()
    {
        int kubernetesId = await GetSkillIdAsync("Kubernetes");
        TaskCompletionSource release = new();
        TaskCompletionSource entered = new();
        Task<int> holder = harness.SkillGate.RunAsync(
            async () =>
            {
                entered.SetResult();
                await release.Task;

                return 0;
            },
            CancellationToken.None);
        await entered.Task;

        Task<int> marking = harness.Skills.SetCvMarkAsync(kubernetesId, CvMark.No);
        await Task.Delay(100);

        Assert.False(marking.IsCompleted);
        Assert.Null((await GetSkillAsync(kubernetesId)).CvMark);

        release.SetResult();
        await holder;
        await marking;

        Assert.Equal(CvMark.No, (await GetSkillAsync(kubernetesId)).CvMark);
    }

    [Fact]
    public async Task MergeAsync_WhenAJobNamesTheMergedSkill_ResolvesItToTheTargetAndReclassifiesWithTheTargetsMark()
    {
        Job job = ScoredJob(JobClass.A, new ScoreSkill("k8s", "primary", "tool", "yes"));
        await harness.SaveAsync(job);
        int kubernetesId = await GetSkillIdAsync("Kubernetes");
        int orchestrationId = await AddSkillAsync("Container orchestration");
        await harness.Skills.SetCvMarkAsync(orchestrationId, CvMark.No);

        int classesChanged = await harness.Skills.MergeAsync(kubernetesId, orchestrationId);

        Job reclassified = await harness.GetJobAsync(job.Id);
        Assert.Equal(1, classesChanged);
        Assert.Equal(JobClass.C, reclassified.Class);
        Assert.Equal<ScoreSkill>([new ScoreSkill("k8s", "primary", "tool", "yes")], reclassified.Score!.Skills);

        Skill orchestration = await GetSkillAsync(orchestrationId);
        Assert.Equal<string>(["container orchestration", "kubernetes", "k8s"], orchestration.Aliases);
        Assert.Equal(SkillCategory.Tool, orchestration.Category);
        Assert.Equal(CvMark.No, orchestration.CvMark);
        Assert.Equal(CvMarkSetBy.Candidate, orchestration.CvMarkSetBy);

        await using JobHunterDbContext context = await harness.ContextFactory.CreateDbContextAsync();
        Assert.False(await context.Skills.AnyAsync(skill => skill.Id == kubernetesId));
    }

    [Fact]
    public async Task MergeAsync_ForASkillIntoItself_RefusesWithoutChangingTheCatalog()
    {
        int kubernetesId = await GetSkillIdAsync("Kubernetes");

        await Assert.ThrowsAsync<ArgumentException>(() => harness.Skills.MergeAsync(kubernetesId, kubernetesId));

        Assert.Equal<string>(["kubernetes", "k8s"], (await GetSkillAsync(kubernetesId)).Aliases);
    }

    private static Job ScoredJob(JobClass jobClass, params ScoreSkill[] skills)
    {
        Job job = ListedJobs.NewInboxJob("Acme", jobClass, null, SeenAt);
        job.RecordScore(job.Score! with { Skills = [.. skills] }, jobClass);

        return job;
    }

    private async Task<int> GetSkillIdAsync(string name)
    {
        await using JobHunterDbContext context = await harness.ContextFactory.CreateDbContextAsync();

        return await context.Skills.Where(skill => skill.Name == name).Select(skill => skill.Id).SingleAsync();
    }

    /// <summary>Adds a skill the way scoring creates one, marked as on the CV by the model, and returns its identifier.</summary>
    private async Task<int> AddSkillAsync(string name)
    {
        await using JobHunterDbContext context = await harness.ContextFactory.CreateDbContextAsync();
        Skill skill = Skill.CreateFromModel(name, SkillCategory.Tool, CvMark.Yes);
        context.Skills.Add(skill);
        await context.SaveChangesAsync();

        return skill.Id;
    }

    private async Task<Skill> GetSkillAsync(int skillId)
    {
        await using JobHunterDbContext context = await harness.ContextFactory.CreateDbContextAsync();

        return await context.Skills.AsNoTracking().SingleAsync(skill => skill.Id == skillId);
    }
}
