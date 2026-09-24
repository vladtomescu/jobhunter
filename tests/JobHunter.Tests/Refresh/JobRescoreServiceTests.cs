using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Llm.Contracts;
using JobHunter.Refresh;
using JobHunter.Tests.Applications;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Tests.Refresh;

/// <summary>Proves what the Score again button does to one job: the same scoring as a Refresh, the triage and the application left alone, and a failed attempt that keeps the score the job already had.</summary>
public sealed class JobRescoreServiceTests
{
    private static readonly DateTimeOffset SeenAt = new(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RescoreAsync_ForAScoredJob_ReplacesTheScoreTheClassAndTheFlags()
    {
        await using RefreshTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = TestJobs.NewScoredJob("Northwind", JobClass.A, SeenAt);
        await harness.SaveAsync(job);
        harness.Scorer.Answer = request => ScoreOutcome.Success(RequiresUsAuthorization(FakeJobScorer.StrongPayload(request.JobId)), FakeJobScorer.ModelName, LlmUsage.None);

        RescoreResult result = await harness.Rescorer.RescoreAsync(job.Id);

        Job stored = await harness.SingleJobAsync();
        Assert.Null(result.FailureReason);
        Assert.Equal(JobClass.C, stored.Class);
        Assert.Equal(JobClass.C, result.Class);
        Assert.Equal(ScoringState.Scored, stored.Scoring);
        Assert.NotNull(stored.Score);
        Assert.Equal(FakeJobScorer.ModelName, stored.Score.Model);
        Assert.Equal(result.Total, stored.Score.Total);
        Assert.Contains(JobFlag.WA, stored.Flags);
        Assert.Equal($"Scored again: {stored.Score.Total} of 14, class C.", result.Describe());
    }

    [Fact]
    public async Task RescoreAsync_ForAPursuedJobWithAnApplication_LeavesTheTriageAndTheApplicationUntouched()
    {
        await using RefreshTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = TestJobs.NewScoredJob("Northwind", JobClass.A, SeenAt);
        job.Pursue(SeenAt.AddDays(1));
        await harness.SaveAsync(job);
        Application application = Application.Create(job.Id, ApplicationStatus.Applied, SeenAt.AddDays(2), "Sent through the form.");
        await SaveAsync(harness, application);
        harness.Scorer.Answer = request => ScoreOutcome.Success(RequiresUsAuthorization(FakeJobScorer.StrongPayload(request.JobId)), FakeJobScorer.ModelName, LlmUsage.None);

        await harness.Rescorer.RescoreAsync(job.Id);

        Job stored = await harness.SingleJobAsync();
        Application storedApplication = await SingleApplicationAsync(harness);
        Assert.Equal(JobClass.C, stored.Class);
        Assert.Equal(TriageState.Pursued, stored.Triage);
        Assert.Equal(SeenAt.AddDays(1), stored.TriagedAt);
        Assert.Equal(application.Id, storedApplication.Id);
        Assert.Equal(ApplicationStatus.Applied, storedApplication.Status);
        Assert.Equal(SeenAt.AddDays(2), storedApplication.StatusChangedAt);
    }

    [Fact]
    public async Task RescoreAsync_WhenTheCallFailsOnAScoredJob_KeepsTheOldScoreAndReturnsTheReason()
    {
        await using RefreshTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = TestJobs.NewScoredJob("Northwind", JobClass.B, SeenAt);
        await harness.SaveAsync(job);
        harness.Scorer.Answer = _ => FakeJobScorer.UsageLimitOutcome();
        string limitReason = FakeJobScorer.UsageLimitOutcome().FailureReason!;

        RescoreResult result = await harness.Rescorer.RescoreAsync(job.Id);

        Job stored = await harness.SingleJobAsync();
        Assert.Equal(limitReason, result.FailureReason);
        Assert.Equal($"Scoring failed: {limitReason}.", result.Describe());
        Assert.Equal(JobClass.B, stored.Class);
        Assert.Equal(ScoringState.Scored, stored.Scoring);
        Assert.Null(stored.ScoreError);
        Assert.NotNull(stored.Score);
        Assert.Equal(job.Score!.Total, stored.Score.Total);
        Assert.Equal(job.Score.Model, stored.Score.Model);
        Assert.Equal(job.Score.ScoredAt, stored.Score.ScoredAt);
        Assert.Equal(TriageState.New, stored.Triage);
    }

    [Fact]
    public async Task RescoreAsync_ForAnUnscoredJob_ScoresIt()
    {
        await using RefreshTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = UnscoredJob();
        await harness.SaveAsync(job);

        RescoreResult result = await harness.Rescorer.RescoreAsync(job.Id);

        Job stored = await harness.SingleJobAsync();
        Assert.Null(result.FailureReason);
        Assert.Equal(ScoringState.Scored, stored.Scoring);
        Assert.Equal(JobClass.A, stored.Class);
        Assert.NotNull(stored.Score);
    }

    [Fact]
    public async Task RescoreAsync_WhenTheCallFailsOnAnUnscoredJob_RecordsTheFailureAsARefreshWould()
    {
        await using RefreshTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = UnscoredJob();
        await harness.SaveAsync(job);
        harness.Scorer.Answer = _ => ScoreOutcome.Failure("the model refused", retryable: false);

        RescoreResult result = await harness.Rescorer.RescoreAsync(job.Id);

        Job stored = await harness.SingleJobAsync();
        Assert.Equal("the model refused", result.FailureReason);
        Assert.Equal(ScoringState.Failed, stored.Scoring);
        Assert.Equal("the model refused", stored.ScoreError);
        Assert.Null(stored.Class);
        Assert.Null(stored.Score);
    }

    [Fact]
    public async Task RescoreAsync_SendsTheScoreModelOfTheSettingsAndIgnoresThePerRunCap()
    {
        await using RefreshTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.Settings.ApplyAsync(settings =>
        {
            settings.ConfigureModels("claude-rescore-test", settings.KitModel);
            settings.ConfigureRunLimits(settings.FirstRunWindowDays, settings.GhostThresholdDays, settings.AutoRefreshAfterHours, 0);
        });
        Job job = TestJobs.NewScoredJob("Northwind", JobClass.A, SeenAt);
        await harness.SaveAsync(job);

        RescoreResult result = await harness.Rescorer.RescoreAsync(job.Id);

        ScoreRequest request = Assert.Single(harness.Scorer.Requests);
        Assert.Equal("claude-rescore-test", request.Model);
        Assert.Equal(job.Id, request.JobId);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public async Task RescoreAsync_WithoutAnApiKey_SendsNothingAndLeavesTheJobAsItWas()
    {
        await using RefreshTestHarness harness = new(apiKeyPresent: false);
        await harness.InitializeAsync();
        Job job = UnscoredJob();
        await harness.SaveAsync(job);

        RescoreResult result = await harness.Rescorer.RescoreAsync(job.Id);

        Job stored = await harness.SingleJobAsync();
        Assert.NotNull(result.FailureReason);
        Assert.Empty(harness.Scorer.Requests);
        Assert.Equal(ScoringState.Unscored, stored.Scoring);
        Assert.Null(stored.ScoreError);
    }

    private static Job UnscoredJob()
    {
        Job job = TestJobs.NewJob("Northwind", [], null, isActive: true, SeenAt);
        job.ApplyPrefilterVerdict(PrefilterState.Passed, null, []);

        return job;
    }

    private static ScorePayload RequiresUsAuthorization(ScorePayload payload)
    {
        return payload with { Facts = payload.Facts with { RequiresUsAuthorization = true } };
    }

    private static async Task SaveAsync(RefreshTestHarness harness, Application application)
    {
        await using JobHunterDbContext context = await harness.ContextFactory.CreateDbContextAsync();
        context.Applications.Add(application);
        await context.SaveChangesAsync();
    }

    private static async Task<Application> SingleApplicationAsync(RefreshTestHarness harness)
    {
        await using JobHunterDbContext context = await harness.ContextFactory.CreateDbContextAsync();

        return await context.Applications.AsNoTracking().SingleAsync();
    }
}
