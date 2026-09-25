using JobHunter.Applications;
using JobHunter.Domain;
using JobHunter.Jobs;
using JobHunter.Llm.Contracts;

namespace JobHunter.Tests.Applications;

/// <summary>Covers Pursue and Skip: the application created with no kit, and every kit outcome WriteKitAsync's writer can return.</summary>
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
    public async Task PursueAsync_OnANewJob_CreatesExactlyOneApplicationAndRequestsNoKit()
    {
        Job job = TestJobs.NewScoredJob("Example Co", JobClass.A, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);

        Application application = await harness.Triage.PursueAsync(job.Id);
        await harness.Triage.PursueAsync(job.Id);

        Assert.Equal(1, await harness.CountApplicationsAsync());
        Assert.Equal(0, harness.KitWriter.CallCount);
        Assert.Equal(KitState.None, application.KitState);
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
    public async Task WriteKitAsync_OnAScoredJob_SendsTheSameKitRequestTheExportWrites()
    {
        Job job = TestJobs.NewScoredJob("Example Co", JobClass.A, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);

        await harness.Triage.PursueAsync(job.Id);
        await harness.Triage.WriteKitAsync(job.Id);

        KitRequest request = Assert.Single(harness.KitWriter.Requests);
        Assert.Equal(job.Id, request.JobId);
        Assert.Equal("A", request.Class);
        Assert.Null(job.ApplyUrl);
        Assert.Equal(job.PostingUrl, request.ApplyUrl);
        Assert.Equal(job.Score!.Total, request.Score.Scores.Niche + request.Score.Scores.Level + request.Score.Scores.Stack + request.Score.Scores.RemoteTimezone + request.Score.Scores.ContractForm + request.Score.Scores.CompSignal + request.Score.Scores.CompanySignal);
        Assert.Equal<string>(["timezone"], request.Score.BlockingUnknowns);
    }

    [Fact]
    public async Task WriteKitAsync_WhenTheKitWriterSucceeds_AttachesTheKitAsReady()
    {
        Job job = TestJobs.NewScoredJob("Example Co", JobClass.A, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);
        JobHunter.Domain.Settings settings = await harness.Settings.GetAsync();

        await harness.Triage.PursueAsync(job.Id);
        Application application = await harness.Triage.WriteKitAsync(job.Id);

        Assert.Equal(KitState.Ready, application.KitState);
        Assert.Null(application.KitError);
        Assert.NotNull(application.Kit);
        Assert.Equal(settings.ResumePdfPath, application.Kit.ResumePath);
        Assert.Empty(application.Kit.LintIssues);
    }

    [Fact]
    public async Task WriteKitAsync_WhenTheKitStillHasLintIssues_MarksKitFailedButKeepsTheText()
    {
        Job job = TestJobs.NewScoredJob("Example Co", JobClass.A, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);
        harness.KitWriter.EnqueueOutcome(request => KitOutcome.Success(
            new KitPayload(request.JobId.ToString(), "en", ["Fit one."], "Cover note.", [], ["Question one"]),
            "claude-opus-5",
            LlmUsage.None,
            ["exclamation mark found"]));

        await harness.Triage.PursueAsync(job.Id);
        Application application = await harness.Triage.WriteKitAsync(job.Id);

        Assert.Equal(KitState.Failed, application.KitState);
        Assert.Equal("exclamation mark found", application.KitError);
        Assert.NotNull(application.Kit);
        Assert.Equal("Cover note.", application.Kit.CoverNote);
        Assert.Equal(ApplicationStatus.Saved, application.Status);
    }

    [Fact]
    public async Task WriteKitAsync_WhenTheKitWriterReturnsFailure_MarksKitFailedAndLeavesTheApplicationAtSaved()
    {
        Job job = TestJobs.NewScoredJob("Example Co", JobClass.A, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);
        harness.KitWriter.EnqueueOutcome(_ => KitOutcome.Failure("rate limited", retryable: true));

        await harness.Triage.PursueAsync(job.Id);
        Application application = await harness.Triage.WriteKitAsync(job.Id);

        Assert.Equal(KitState.Failed, application.KitState);
        Assert.Equal("rate limited", application.KitError);
        Assert.Null(application.Kit);
        Assert.Equal(ApplicationStatus.Saved, application.Status);
    }

    [Fact]
    public async Task WriteKitAsync_WhenTheKitWriterThrows_MarksKitFailedAndLeavesTheApplicationAtSaved()
    {
        Job job = TestJobs.NewScoredJob("Example Co", JobClass.A, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);
        harness.KitWriter.ExceptionToThrowOnNextCall = new InvalidOperationException("network unreachable");

        await harness.Triage.PursueAsync(job.Id);
        Application application = await harness.Triage.WriteKitAsync(job.Id);

        Assert.Equal(KitState.Failed, application.KitState);
        Assert.Equal("network unreachable", application.KitError);
        Assert.Null(application.Kit);
        Assert.Equal(ApplicationStatus.Saved, application.Status);
    }

    [Fact]
    public async Task WriteKitAsync_OnAnUnscoredJob_MarksKitFailedWithoutCallingTheWriter()
    {
        Job job = TestJobs.NewJob("Example Co", [], null, isActive: true, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);

        await harness.Triage.PursueAsync(job.Id);
        Application application = await harness.Triage.WriteKitAsync(job.Id);

        Assert.Equal(KitState.Failed, application.KitState);
        Assert.Equal(0, harness.KitWriter.CallCount);
    }

    [Fact]
    public async Task WriteKitAsync_OnAFailedKit_WritesTheKitAndMarksItReady()
    {
        Job job = TestJobs.NewScoredJob("Example Co", JobClass.A, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);
        harness.KitWriter.EnqueueOutcome(_ => KitOutcome.Failure("rate limited", retryable: true));
        await harness.Triage.PursueAsync(job.Id);
        Application application = await harness.Triage.WriteKitAsync(job.Id);
        Assert.Equal(KitState.Failed, application.KitState);

        Application rewritten = await harness.Triage.WriteKitAsync(job.Id);

        Assert.Equal(KitState.Ready, rewritten.KitState);
        Assert.Null(rewritten.KitError);
        Assert.NotNull(rewritten.Kit);
        Assert.Equal(2, harness.KitWriter.CallCount);
    }

    [Fact]
    public async Task WriteKitAsync_OnAReadyKit_DoesNothingAndLeavesTheKitUnchanged()
    {
        Job job = TestJobs.NewScoredJob("Example Co", JobClass.A, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);
        await harness.Triage.PursueAsync(job.Id);
        Application application = await harness.Triage.WriteKitAsync(job.Id);
        Assert.Equal(KitState.Ready, application.KitState);

        Application unchanged = await harness.Triage.WriteKitAsync(job.Id);

        Assert.Equal(KitState.Ready, unchanged.KitState);
        Assert.Equal(application.Kit!.GeneratedAt, unchanged.Kit!.GeneratedAt);
        Assert.Equal(1, harness.KitWriter.CallCount);
    }

    [Fact]
    public async Task WriteKitAsync_OnAGeneratingKit_DoesNothingAndLeavesTheStateUnchanged()
    {
        Job job = TestJobs.NewScoredJob("Example Co", JobClass.A, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);
        Application application = Application.Create(job.Id, ApplicationStatus.Saved, DateTimeOffset.UtcNow, "Pursued from the inbox.");
        application.BeginKit();
        await harness.SaveAsync(application);

        Application unchanged = await harness.Triage.WriteKitAsync(job.Id);

        Assert.Equal(KitState.Generating, unchanged.KitState);
        Assert.Equal(0, harness.KitWriter.CallCount);
    }

    [Fact]
    public async Task WriteKitAsync_OnAFailedKit_LeavesTheApplicationStatusAndHistoryUntouched()
    {
        Job job = TestJobs.NewScoredJob("Example Co", JobClass.A, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);
        harness.KitWriter.EnqueueOutcome(_ => KitOutcome.Failure("rate limited", retryable: true));
        await harness.Triage.PursueAsync(job.Id);
        Application application = await harness.Triage.WriteKitAsync(job.Id);

        Application rewritten = await harness.Triage.WriteKitAsync(job.Id);

        Assert.Equal(application.Status, rewritten.Status);
        Assert.Equal(ApplicationStatus.Saved, Assert.Single(rewritten.History).Status);
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

    [Fact]
    public async Task UnpursueAsync_OnASavedApplicationWithAKitAndNotes_DeletesTheApplicationAndReturnsTheJobToNew()
    {
        Job job = TestJobs.NewScoredJob("Example Co", JobClass.A, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);
        Application application = await harness.Triage.PursueAsync(job.Id);
        await harness.Triage.WriteKitAsync(job.Id);
        await harness.Applications.AddNoteAsync(application.Id, "Asked a friend about the team.");
        Application prepared = await harness.GetApplicationAsync(application.Id);
        Assert.NotNull(prepared.Kit);
        Assert.Single(prepared.Notes);

        UnpursueResult result = await harness.Triage.UnpursueAsync(job.Id);

        Assert.True(result.IsUnpursued);
        Assert.Null(await harness.FindApplicationByJobAsync(job.Id));
        Assert.Equal(0, await harness.CountApplicationsAsync());
        Job stored = await harness.GetJobAsync(job.Id);
        Assert.Equal(TriageState.New, stored.Triage);
        Assert.Null(stored.TriagedAt);
    }

    [Fact]
    public async Task UnpursueAsync_OnAnApplicationPastSaved_RefusesAndChangesNothing()
    {
        Job job = TestJobs.NewScoredJob("Example Co", JobClass.A, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);
        Application application = await harness.Triage.PursueAsync(job.Id);
        await harness.Applications.ChangeStatusAsync(application.Id, ApplicationStatus.Applied, null);

        UnpursueResult result = await harness.Triage.UnpursueAsync(job.Id);

        Assert.False(result.IsUnpursued);
        Assert.NotNull(result.Refusal);
        Assert.Equal(ApplicationStatus.Applied, (await harness.GetApplicationAsync(application.Id)).Status);
        Assert.Equal(TriageState.Pursued, (await harness.GetJobAsync(job.Id)).Triage);
    }

    [Fact]
    public async Task UnpursueAsync_OnAJobWithoutAnApplication_RefusesAndChangesNothing()
    {
        Job job = TestJobs.NewScoredJob("Example Co", JobClass.A, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);
        await harness.Triage.SkipAsync(job.Id);

        UnpursueResult result = await harness.Triage.UnpursueAsync(job.Id);

        Assert.False(result.IsUnpursued);
        Assert.NotNull(result.Refusal);
        Assert.Equal(TriageState.Skipped, (await harness.GetJobAsync(job.Id)).Triage);
    }

    [Fact]
    public async Task UnpursueAsync_OnAPursuedJob_PutsTheJobBackInTheInbox()
    {
        Job job = TestJobs.NewScoredJob("Example Co", JobClass.A, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);
        JobQueryService queries = new(harness.ContextFactory);
        await harness.Triage.PursueAsync(job.Id);
        Assert.DoesNotContain(await queries.GetInboxAsync(), row => row.Id == job.Id);

        await harness.Triage.UnpursueAsync(job.Id);

        Assert.Contains(await queries.GetInboxAsync(), row => row.Id == job.Id);
    }
}
