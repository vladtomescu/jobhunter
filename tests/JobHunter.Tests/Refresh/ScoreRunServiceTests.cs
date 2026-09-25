using JobHunter.Domain;
using JobHunter.Llm.Contracts;
using JobHunter.Pipeline;
using JobHunter.Refresh;
using JobHunter.Sources;

namespace JobHunter.Tests.Refresh;

/// <summary>Proves what one score run does: which jobs it sends and in what order, the cap, the missing key, failures and the usage-limit halt, the gate it shares with the refresh, and the run record it leaves; jobs are brought in by a refresh over fake sources first.</summary>
public sealed class ScoreRunServiceTests
{
    private const string FirstUrl = "https://boards.greenhouse.io/northwind/jobs/1";

    private static readonly string[] DistinctCompanies = ["Northwind", "Contoso", "Fabrikam", "Tailspin", "Litware", "Adatum", "Proseware", "Wingtip", "Lucerne", "Margie"];

    [Fact]
    public async Task RunAsync_WithAManualJobAmongMoreCandidatesThanTheCap_ScoresTheManualJobFirst()
    {
        FakeJobSource source = new(JobSourceKind.RemoteOk);
        source.Returns(DistinctPostings(5));
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();
        await harness.SaveAsync(RefreshTestHarness.ManualInboxJob(DateTimeOffset.UtcNow.AddDays(-3)));
        await harness.Settings.ApplyAsync(settings => settings.ConfigureRunLimits(21, 21, 12, 2));
        await harness.RunAsync();

        RefreshResult result = await harness.ScoreAsync();

        List<Job> jobs = await harness.JobsAsync();
        Job manual = jobs.Single(job => job.IsManual);
        Assert.NotNull(result.Summary);
        Assert.Equal(6, jobs.Count);
        Assert.Equal(2, result.Summary.Scored);
        Assert.Null(manual.PostedAt);
        Assert.Equal(ScoringState.Scored, manual.Scoring);
    }

    [Fact]
    public async Task RunAsync_WithMoreUnscoredJobsThanTheCap_ScoresOnlyTheCap()
    {
        FakeJobSource source = new(JobSourceKind.RemoteOk);
        source.Returns(DistinctPostings(5));
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();
        await harness.Settings.ApplyAsync(settings => settings.ConfigureRunLimits(21, 21, 12, 2));
        await harness.RunAsync();

        RefreshResult result = await harness.ScoreAsync();

        List<Job> jobs = await harness.JobsAsync();
        Assert.NotNull(result.Summary);
        Assert.Equal(5, jobs.Count);
        Assert.Equal(2, result.Summary.Scored);
        Assert.Equal(2, harness.Scorer.Requests.Count);
        Assert.Equal(2, jobs.Count(job => job.Scoring == ScoringState.Scored));
        Assert.Equal(3, await harness.Backlog.CountAsync());
        Assert.Equal(3, harness.State.AwaitingScore);
    }

    [Fact]
    public async Task RunAsync_WithTheCapAtZero_SendsNothing()
    {
        FakeJobSource source = new(JobSourceKind.RemoteOk);
        source.Returns(DistinctPostings(2));
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();
        await harness.Settings.ApplyAsync(settings => settings.ConfigureRunLimits(21, 21, 12, 0));
        await harness.RunAsync();

        RefreshResult result = await harness.ScoreAsync();

        Assert.NotNull(result.Summary);
        Assert.Equal(FetchOutcome.Completed, result.Summary.Outcome);
        Assert.Equal(0, result.Summary.Scored);
        Assert.Empty(harness.Scorer.Requests);
        Assert.Equal(2, await harness.Backlog.CountAsync());
    }

    [Fact]
    public async Task RunAsync_WithoutAnApiKey_SendsNothingAndLeavesTheJobsUnscored()
    {
        FakeJobSource source = new(JobSourceKind.RemoteOk);
        source.Returns(TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-1", FirstUrl));
        await using RefreshTestHarness harness = new(apiKeyPresent: false, source);
        await harness.InitializeAsync();
        await harness.RunAsync();

        RefreshResult result = await harness.ScoreAsync();

        Job job = await harness.SingleJobAsync();
        Assert.NotNull(result.Summary);
        Assert.Equal(FetchOutcome.Completed, result.Summary.Outcome);
        Assert.Equal(0, result.Summary.Scored);
        Assert.Empty(harness.Scorer.Requests);
        Assert.Equal(ScoringState.Unscored, job.Scoring);
        Assert.Equal(1, await harness.Backlog.CountAsync());
    }

    [Fact]
    public async Task RunAsync_AfterARefreshChangedTheDescription_ScoresTheJobAgain()
    {
        const string rewritten = "<p>We run a distributed platform on .NET and are rewriting the ingestion path.</p>";
        FakeJobSource source = new(JobSourceKind.RemoteOk);
        source.Returns(TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-1", FirstUrl));
        source.Returns(TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-1", FirstUrl, description: rewritten));
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();

        await harness.RunAsync();
        RefreshResult first = await harness.ScoreAsync();
        await harness.RunAsync();
        RefreshResult second = await harness.ScoreAsync();

        Job job = await harness.SingleJobAsync();
        Assert.NotNull(first.Summary);
        Assert.NotNull(second.Summary);
        Assert.Equal(1, first.Summary.Scored);
        Assert.Equal(1, second.Summary.Scored);
        Assert.Equal(2, harness.Scorer.Requests.Count);
        Assert.NotNull(job.Score);
        Assert.Equal(JobFingerprint.ForDescription(HtmlToText.Convert(rewritten)), job.Score.DescriptionHashAtScoring);
    }

    [Fact]
    public async Task RunAsync_WithNothingWaiting_SendsNothing()
    {
        FakeJobSource source = new(JobSourceKind.RemoteOk);
        source.Returns(TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-1", FirstUrl));
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();
        await harness.RunAsync();
        await harness.ScoreAsync();

        RefreshResult second = await harness.ScoreAsync();

        Assert.NotNull(second.Summary);
        Assert.Equal(0, second.Summary.Scored);
        Assert.Single(harness.Scorer.Requests);
        Assert.Equal(0, harness.State.AwaitingScore);
    }

    [Fact]
    public async Task RunAsync_WhenAScoringCallFails_RecordsTheFailureAndLeavesTheOtherJobScored()
    {
        FakeJobSource source = new(JobSourceKind.RemoteOk);
        source.Returns(DistinctPostings(2));
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();
        harness.Scorer.Answer = request => request.Company == DistinctCompanies[1]
            ? ScoreOutcome.Failure("the model refused", retryable: false)
            : ScoreOutcome.Success(FakeJobScorer.StrongPayload(request.JobId), FakeJobScorer.ModelName, LlmUsage.None);
        await harness.RunAsync();

        RefreshResult result = await harness.ScoreAsync();

        List<Job> jobs = await harness.JobsAsync();
        Assert.NotNull(result.Summary);
        Assert.Equal(1, result.Summary.Scored);
        Assert.Equal(1, result.Summary.ScoreFailures);
        Assert.Equal("the model refused", jobs.Single(job => job.Company == DistinctCompanies[1]).ScoreError);
        Assert.Equal(JobClass.A, jobs.Single(job => job.Company == DistinctCompanies[0]).Class);
    }

    [Fact]
    public async Task RunAsync_WhenScoringCallsFail_RecordsTheReasonsGroupedOnTheRun()
    {
        FakeJobSource source = new(JobSourceKind.RemoteOk);
        source.Returns(DistinctPostings(3));
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();
        harness.Scorer.Answer = request => request.Company == DistinctCompanies[0]
            ? ScoreOutcome.Success(FakeJobScorer.StrongPayload(request.JobId), FakeJobScorer.ModelName, LlmUsage.None)
            : ScoreOutcome.Failure("the model refused", retryable: false);
        await harness.RunAsync();

        RefreshResult result = await harness.ScoreAsync();

        FetchRun stored = await StoredScoreRunAsync(harness);
        Assert.Equal(new ScoringFailureReason("the model refused", 2), Assert.Single(stored.ScoringFailureReasons));
        Assert.Null(stored.ScoringHaltReason);
        Assert.NotNull(result.Summary);
        Assert.Equal(new ScoringFailureReason("the model refused", 2), Assert.Single(result.Summary.ScoringFailureReasons));
    }

    [Fact]
    public async Task RunAsync_WhenTheAccountUsageLimitIsReached_StopsSendingAndLeavesTheUnsentJobsUnscored()
    {
        const int jobCount = 8;
        FakeJobSource source = new(JobSourceKind.RemoteOk);
        source.Returns(DistinctPostings(jobCount));
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();
        harness.Scorer.Answer = _ => FakeJobScorer.UsageLimitOutcome();
        await harness.RunAsync();

        RefreshResult result = await harness.ScoreAsync();

        int sent = harness.Scorer.Requests.Count;
        List<Job> jobs = await harness.JobsAsync();
        FetchRun stored = await StoredScoreRunAsync(harness);
        string limitReason = FakeJobScorer.UsageLimitOutcome().FailureReason!;
        Assert.InRange(sent, 1, ScoreRunService.ScoringConcurrency);
        Assert.Equal(sent, jobs.Count(job => job.Scoring == ScoringState.Failed && job.ScoreError == limitReason));
        Assert.Equal(jobCount - sent, jobs.Count(job => job.Scoring == ScoringState.Unscored && job.ScoreError is null));
        Assert.Equal(limitReason, stored.ScoringHaltReason);
        Assert.Equal(new ScoringFailureReason(limitReason, sent), Assert.Single(stored.ScoringFailureReasons));
        Assert.Equal(sent, stored.ScoreFailures);
        Assert.Equal(FetchOutcome.Completed, stored.Outcome);
        Assert.NotNull(result.Summary);
        Assert.Equal(limitReason, result.Summary.ScoringHaltReason);
        Assert.Equal(jobCount, await harness.Backlog.CountAsync());
    }

    [Fact]
    public async Task RunAsync_WhenEveryCallInFlightMeetsTheUsageLimit_LetsThoseFinishAndStartsNoOther()
    {
        const int jobCount = 9;
        FakeJobSource source = new(JobSourceKind.RemoteOk);
        source.Returns(DistinctPostings(jobCount));
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();
        int arrived = 0;
        TaskCompletionSource allInFlight = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Scorer.AnswerAsync = async _ =>
        {
            if (Interlocked.Increment(ref arrived) == ScoreRunService.ScoringConcurrency)
            {
                allInFlight.TrySetResult();
            }

            await allInFlight.Task.WaitAsync(TimeSpan.FromSeconds(10));

            return FakeJobScorer.UsageLimitOutcome();
        };
        await harness.RunAsync();

        await harness.ScoreAsync();

        List<Job> jobs = await harness.JobsAsync();
        Assert.Equal(ScoreRunService.ScoringConcurrency, harness.Scorer.Requests.Count);
        Assert.Equal(ScoreRunService.ScoringConcurrency, jobs.Count(job => job.Scoring == ScoringState.Failed));
        Assert.Equal(jobCount - ScoreRunService.ScoringConcurrency, jobs.Count(job => job.Scoring == ScoringState.Unscored));
    }

    [Fact]
    public async Task RunAsync_AfterAUsageLimitStop_ScoresTheJobsLeftBehindOnTheNextScoreRun()
    {
        const int jobCount = 6;
        FakeJobSource source = new(JobSourceKind.RemoteOk);
        source.Returns(DistinctPostings(jobCount));
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();
        harness.Scorer.Answer = _ => FakeJobScorer.UsageLimitOutcome();
        await harness.RunAsync();
        await harness.ScoreAsync();
        harness.Scorer.Answer = null;

        RefreshResult second = await harness.ScoreAsync();

        Assert.NotNull(second.Summary);
        Assert.Equal(jobCount, second.Summary.Scored);
        Assert.Null(second.Summary.ScoringHaltReason);
        Assert.All(await harness.JobsAsync(), job => Assert.Equal(ScoringState.Scored, job.Scoring));
    }

    [Fact]
    public async Task RunAsync_WhenItFinishes_RecordsTheRunWithTheScoreTriggerItsCountsAndNoSources()
    {
        FakeJobSource source = new(JobSourceKind.RemoteOk);
        source.Returns(DistinctPostings(3));
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();
        harness.Scorer.Answer = request => request.Company == DistinctCompanies[2]
            ? ScoreOutcome.Failure("the model refused", retryable: false)
            : ScoreOutcome.Success(FakeJobScorer.StrongPayload(request.JobId), FakeJobScorer.ModelName, LlmUsage.None);
        await harness.RunAsync();

        RefreshResult result = await harness.ScoreAsync();

        RefreshRunSummary listed = (await harness.RunHistory.GetRunsAsync())[0];
        Assert.NotNull(result.Summary);
        Assert.Equal(result.Summary.RunId, listed.RunId);
        Assert.Equal(FetchTrigger.Score, listed.Trigger);
        Assert.Equal(FetchOutcome.Completed, listed.Outcome);
        Assert.Equal(2, listed.Scored);
        Assert.Equal(1, listed.ScoreFailures);
        Assert.Equal(new ScoringFailureReason("the model refused", 1), Assert.Single(listed.ScoringFailureReasons));
        Assert.Empty(listed.SourceResults);
        Assert.Equal(0, listed.NewJobs);
        Assert.NotNull(listed.FinishedAt);
    }

    [Fact]
    public async Task RunAsync_WhileItRuns_PublishesTheScoringProgressAndNotTheFetchOnTheSharedState()
    {
        FakeJobSource source = new(JobSourceKind.RemoteOk);
        source.Returns(DistinctPostings(2));
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();
        await harness.RunAsync();
        Lock phaseGate = new();
        List<RefreshPhase> phases = [];
        harness.State.Changed += () =>
        {
            lock (phaseGate)
            {
                phases.Add(harness.State.Phase);
            }
        };

        await harness.ScoreAsync();

        Assert.False(harness.State.IsRunning);
        Assert.Contains(RefreshPhase.Scoring, phases);
        Assert.DoesNotContain(RefreshPhase.Fetching, phases);
        Assert.NotNull(harness.State.LastRun);
        Assert.Equal(FetchTrigger.Score, harness.State.LastRun.Trigger);
        Assert.Equal(2, harness.State.LastRun.Scored);
        Assert.Equal(0, harness.State.AwaitingScore);
    }

    [Fact]
    public async Task RunAsync_WhileARefreshIsInProgress_IsRefused()
    {
        TaskCompletionSource fetching = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeJobSource source = new(JobSourceKind.RemoteOk)
        {
            Gate = async () =>
            {
                fetching.TrySetResult();
                await release.Task;
            }
        };
        source.Returns(TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-1", FirstUrl));
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();
        await harness.SaveAsync(RefreshTestHarness.ManualInboxJob(DateTimeOffset.UtcNow.AddDays(-1)));

        Task<RefreshResult> refresh = harness.RunAsync();
        await fetching.Task;
        RefreshResult scoreRun = await harness.ScoreAsync();
        release.TrySetResult();
        RefreshResult completed = await refresh;

        Assert.Equal(RunGate.RefreshRunningMessage, scoreRun.Refusal);
        Assert.False(scoreRun.Started);
        Assert.True(completed.Started);
        Assert.Empty(harness.Scorer.Requests);
        Assert.Equal(FetchTrigger.Manual, Assert.Single(await harness.RunsAsync()).Trigger);
    }

    [Fact]
    public async Task RunAsync_WhileAScoreRunIsInProgress_RefusesTheSecondCall()
    {
        TaskCompletionSource scoring = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using RefreshTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(RefreshTestHarness.ManualInboxJob(DateTimeOffset.UtcNow.AddDays(-1)));
        harness.Scorer.AnswerAsync = async request =>
        {
            scoring.TrySetResult();
            await release.Task;

            return ScoreOutcome.Success(FakeJobScorer.StrongPayload(request.JobId), FakeJobScorer.ModelName, LlmUsage.None);
        };

        Task<RefreshResult> first = harness.ScoreAsync();
        await scoring.Task;
        RefreshResult second = await harness.ScoreAsync();
        release.TrySetResult();
        RefreshResult completed = await first;

        Assert.Equal(RunGate.ScoreRunningMessage, second.Refusal);
        Assert.False(second.Started);
        Assert.True(completed.Started);
        Assert.Single(harness.Scorer.Requests);
        Assert.Single(await harness.RunsAsync());
    }

    private static async Task<FetchRun> StoredScoreRunAsync(RefreshTestHarness harness)
    {
        return Assert.Single(await harness.RunsAsync(), run => run.Trigger == FetchTrigger.Score);
    }

    /// <summary>Postings of different companies, so that no two of them merge as duplicates.</summary>
    private static RawJob[] DistinctPostings(int count)
    {
        return [.. DistinctCompanies.Take(count).Select((company, index) => TestPostings.Posting(JobSourceKind.RemoteOk, $"remoteok-{index}", $"https://jobs.example.com/{index}", company: company))];
    }
}
