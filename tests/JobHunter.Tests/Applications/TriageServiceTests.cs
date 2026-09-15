using JobHunter.Domain;
using JobHunter.Llm.Contracts;

namespace JobHunter.Tests.Applications;

/// <summary>Covers Pursue and Skip: the application created, the kit requested, and every kit outcome the writer can return.</summary>
public sealed class TriageServiceTests : IAsyncLifetime
{
    private readonly ApplicationsTestHarness harness = new();

    public Task InitializeAsync()
    {
        return harness.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        return harness.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task PursueAsync_OnANewJob_CreatesExactlyOneApplicationAndRequestsExactlyOneKit()
    {
        Job job = TestJobs.NewScoredJob("Example Co", JobClass.A, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);

        await harness.Triage.PursueAsync(job.Id);
        await harness.Triage.PursueAsync(job.Id);

        Assert.Equal(1, await harness.CountApplicationsAsync());
        Assert.Equal(1, harness.KitWriter.CallCount);
    }

    [Fact]
    public async Task PursueAsync_OnAJobAlreadyPursued_ReturnsTheExistingApplication()
    {
        Job job = TestJobs.NewScoredJob("Example Co", JobClass.A, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);

        Application first = await harness.Triage.PursueAsync(job.Id);
        Application second = await harness.Triage.PursueAsync(job.Id);

        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task PursueAsync_OnANewJob_MarksTheJobAsPursued()
    {
        Job job = TestJobs.NewScoredJob("Example Co", JobClass.A, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);

        await harness.Triage.PursueAsync(job.Id);

        Job stored = await harness.GetJobAsync(job.Id);
        Assert.Equal(TriageState.Pursued, stored.Triage);
        Assert.NotNull(stored.TriagedAt);
    }

    [Fact]
    public async Task PursueAsync_OnANewJob_CreatesTheApplicationAtSavedWithAHistoryRow()
    {
        Job job = TestJobs.NewScoredJob("Example Co", JobClass.A, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);

        Application application = await harness.Triage.PursueAsync(job.Id);

        Assert.Equal(job.Id, application.JobId);
        Assert.Equal(ApplicationStatus.Saved, application.Status);
        Assert.Equal(ApplicationStatus.Saved, Assert.Single(application.History).Status);
    }

    [Fact]
    public async Task PursueAsync_OnAScoredJob_SendsTheSameKitRequestTheExportWrites()
    {
        Job job = TestJobs.NewScoredJob("Example Co", JobClass.A, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);

        await harness.Triage.PursueAsync(job.Id);

        KitRequest request = Assert.Single(harness.KitWriter.Requests);
        Assert.Equal(job.Id, request.JobId);
        Assert.Equal("A", request.Class);
        Assert.Null(job.ApplyUrl);
        Assert.Equal(job.PostingUrl, request.ApplyUrl);
        Assert.Equal(job.Score!.Total, request.Score.Scores.Niche + request.Score.Scores.Level + request.Score.Scores.Stack + request.Score.Scores.RemoteTimezone + request.Score.Scores.ContractForm + request.Score.Scores.CompSignal + request.Score.Scores.CompanySignal);
        Assert.Equal<string>(["timezone"], request.Score.BlockingUnknowns);
    }

    [Fact]
    public async Task PursueAsync_WhenTheKitWriterSucceeds_AttachesTheKitAsReady()
    {
        Job job = TestJobs.NewScoredJob("Example Co", JobClass.A, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);
        JobHunter.Domain.Settings settings = await harness.Settings.GetAsync();

        Application application = await harness.Triage.PursueAsync(job.Id);

        Assert.Equal(KitState.Ready, application.KitState);
        Assert.Null(application.KitError);
        Assert.NotNull(application.Kit);
        Assert.Equal(settings.ResumePdfPath, application.Kit.ResumePath);
        Assert.Empty(application.Kit.LintIssues);
    }

    [Fact]
    public async Task PursueAsync_WhenTheKitStillHasLintIssues_MarksKitFailedButKeepsTheText()
    {
        Job job = TestJobs.NewScoredJob("Example Co", JobClass.A, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);
        harness.KitWriter.EnqueueOutcome(request => KitOutcome.Success(
            new KitPayload(request.JobId.ToString(), "en", ["Fit one."], "Cover note.", [], ["Question one"]),
            "claude-opus-5",
            LlmUsage.None,
            ["exclamation mark found"]));

        Application application = await harness.Triage.PursueAsync(job.Id);

        Assert.Equal(KitState.Failed, application.KitState);
        Assert.Equal("exclamation mark found", application.KitError);
        Assert.NotNull(application.Kit);
        Assert.Equal("Cover note.", application.Kit.CoverNote);
        Assert.Equal(ApplicationStatus.Saved, application.Status);
    }

    [Fact]
    public async Task PursueAsync_WhenTheKitWriterReturnsFailure_MarksKitFailedAndLeavesTheApplicationAtSaved()
    {
        Job job = TestJobs.NewScoredJob("Example Co", JobClass.A, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);
        harness.KitWriter.EnqueueOutcome(_ => KitOutcome.Failure("rate limited", retryable: true));

        Application application = await harness.Triage.PursueAsync(job.Id);

        Assert.Equal(KitState.Failed, application.KitState);
        Assert.Equal("rate limited", application.KitError);
        Assert.Null(application.Kit);
        Assert.Equal(ApplicationStatus.Saved, application.Status);
    }

    [Fact]
    public async Task PursueAsync_WhenTheKitWriterThrows_MarksKitFailedAndLeavesTheApplicationAtSaved()
    {
        Job job = TestJobs.NewScoredJob("Example Co", JobClass.A, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);
        harness.KitWriter.ExceptionToThrowOnNextCall = new InvalidOperationException("network unreachable");

        Application application = await harness.Triage.PursueAsync(job.Id);

        Assert.Equal(KitState.Failed, application.KitState);
        Assert.Equal("network unreachable", application.KitError);
        Assert.Null(application.Kit);
        Assert.Equal(ApplicationStatus.Saved, application.Status);
    }

    [Fact]
    public async Task PursueAsync_OnAnUnscoredJob_MarksKitFailedWithoutCallingTheWriter()
    {
        Job job = TestJobs.NewJob("Example Co", [], null, isActive: true, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);

        Application application = await harness.Triage.PursueAsync(job.Id);

        Assert.Equal(KitState.Failed, application.KitState);
        Assert.Equal(0, harness.KitWriter.CallCount);
    }

    [Fact]
    public async Task SkipAsync_OnANewJob_MarksTheJobAsSkippedAndCreatesNoApplication()
    {
        Job job = TestJobs.NewScoredJob("Example Co", JobClass.B, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);

        await harness.Triage.SkipAsync(job.Id);

        Job stored = await harness.GetJobAsync(job.Id);
        Assert.Equal(TriageState.Skipped, stored.Triage);
        Assert.Equal(0, await harness.CountApplicationsAsync());
        Assert.Equal(0, harness.KitWriter.CallCount);
    }
}
