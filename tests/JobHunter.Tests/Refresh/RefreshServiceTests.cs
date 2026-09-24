using JobHunter.Domain;
using JobHunter.Llm.Contracts;
using JobHunter.Pipeline;
using JobHunter.Refresh;
using JobHunter.Sources;

namespace JobHunter.Tests.Refresh;

/// <summary>Proves what one refresh does to the stored jobs: insert, merge, prefilter, liveness, aging and scoring, all against fake sources and a fake scorer.</summary>
public sealed class RefreshServiceTests
{
    private const string FirstUrl = "https://boards.greenhouse.io/northwind/jobs/1";
    private const string SecondUrl = "https://jobs.northwind.example/careers/senior-backend-engineer";

    private static readonly string[] DistinctCompanies = ["Northwind", "Contoso", "Fabrikam", "Tailspin", "Litware", "Adatum", "Proseware", "Wingtip", "Lucerne", "Margie"];

    [Fact]
    public async Task RunAsync_ForANewPosting_InsertsThePassedJobWithItsSource()
    {
        FakeJobSource source = new(JobSourceKind.RemoteOk);
        source.Returns(TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-1", FirstUrl));
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();

        RefreshResult result = await harness.RunAsync();

        Job job = await harness.SingleJobAsync();
        Assert.Equal("Northwind", job.Company);
        Assert.Equal(PrefilterState.Passed, job.Prefilter);
        Assert.Equal(JobSourceKind.RemoteOk, Assert.Single(job.Sources).Kind);
        Assert.Equal(AtsKind.Greenhouse, job.Ats);
        Assert.NotNull(result.Summary);
        Assert.Equal(1, result.Summary.NewJobs);
        Assert.Equal(FetchOutcome.Completed, result.Summary.Outcome);
    }

    [Fact]
    public async Task RunAsync_ForTheSamePostingTwice_MergesOnTheFingerprintWithoutInserting()
    {
        FakeJobSource source = new(JobSourceKind.RemoteOk);
        source.Returns(TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-1", FirstUrl));
        source.Returns(TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-1", $"{FirstUrl}?utm_source=newsletter"));
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();

        await harness.RunAsync();
        RefreshResult second = await harness.RunAsync();

        Assert.Single(await harness.JobsAsync());
        Assert.NotNull(second.Summary);
        Assert.Equal(0, second.Summary.NewJobs);
        Assert.Equal(1, second.Summary.UpdatedJobs);
        Assert.Equal(0, second.Summary.Scored);
    }

    [Fact]
    public async Task RunAsync_ForTheSameRoleUnderAnotherUrl_MergesTheFuzzyDuplicate()
    {
        FakeJobSource source = new(JobSourceKind.RemoteOk);
        source.Returns(TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-1", FirstUrl));
        source.Returns(TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-2", SecondUrl));
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();

        await harness.RunAsync();
        RefreshResult second = await harness.RunAsync();

        Job job = await harness.SingleJobAsync();
        Assert.Equal(2, job.Sources.Count);
        Assert.NotNull(second.Summary);
        Assert.Equal(0, second.Summary.NewJobs);
        Assert.Equal(1, second.Summary.UpdatedJobs);
    }

    [Fact]
    public async Task RunAsync_WhenTheDescriptionChanged_ScoresTheJobAgain()
    {
        const string rewritten = "<p>We run a distributed platform on .NET and are rewriting the ingestion path.</p>";
        FakeJobSource source = new(JobSourceKind.RemoteOk);
        source.Returns(TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-1", FirstUrl));
        source.Returns(TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-1", FirstUrl, description: rewritten));
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();

        RefreshResult first = await harness.RunAsync();
        RefreshResult second = await harness.RunAsync();

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
    public async Task RunAsync_WhenASnapshotSourceStopsListingAJob_MarksItInactiveOnTheSecondMiss()
    {
        FakeJobSource source = new(JobSourceKind.Dataset, isFullSnapshot: true);
        source.Returns(
            TestPostings.Posting(JobSourceKind.Dataset, "greenhouse:1", FirstUrl),
            TestPostings.Posting(JobSourceKind.Dataset, "greenhouse:2", SecondUrl, company: "Contoso", title: "Platform Engineer"));
        source.Returns(TestPostings.Posting(JobSourceKind.Dataset, "greenhouse:2", SecondUrl, company: "Contoso", title: "Platform Engineer"));
        source.Returns(TestPostings.Posting(JobSourceKind.Dataset, "greenhouse:2", SecondUrl, company: "Contoso", title: "Platform Engineer"));
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();

        await harness.RunAsync();
        RefreshResult second = await harness.RunAsync();
        Job afterOneMiss = await harness.JobByPostingUrlAsync(FirstUrl);
        RefreshResult third = await harness.RunAsync();
        Job afterTwoMisses = await harness.JobByPostingUrlAsync(FirstUrl);

        Assert.NotNull(second.Summary);
        Assert.NotNull(third.Summary);
        Assert.Equal(0, second.Summary.MarkedInactive);
        Assert.True(afterOneMiss.IsActive);
        Assert.Equal(1, afterOneMiss.MissedRuns);
        Assert.Equal(1, third.Summary.MarkedInactive);
        Assert.False(afterTwoMisses.IsActive);
    }

    [Fact]
    public async Task RunAsync_WhenASnapshotSourceStopsListingAJobOlderThanTheHorizon_KeepsItActive()
    {
        DateTimeOffset seenAt = DateTimeOffset.UtcNow.AddHours(-1);
        FakeJobSource source = new(JobSourceKind.Dataset, isFullSnapshot: true);
        source.Returns(TestPostings.Posting(JobSourceKind.Dataset, "greenhouse:anchor", FirstUrl));
        source.Returns(TestPostings.Posting(JobSourceKind.Dataset, "greenhouse:anchor", FirstUrl));
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();
        await harness.SaveAsync(SnapshotJob("aged-fingerprint", "https://jobs.example.com/aged", "Oldfield", "Staff Reliability Engineer", DateTimeOffset.UtcNow.AddDays(-40), seenAt));
        await harness.SaveAsync(SnapshotJob("recent-fingerprint", "https://jobs.example.com/recent", "Newbridge", "Principal Data Engineer", DateTimeOffset.UtcNow.AddDays(-5), seenAt));

        RefreshResult first = await harness.RunAsync();
        RefreshResult second = await harness.RunAsync();

        Job aged = await harness.JobByPostingUrlAsync("https://jobs.example.com/aged");
        Job recent = await harness.JobByPostingUrlAsync("https://jobs.example.com/recent");
        Assert.NotNull(first.Summary);
        Assert.NotNull(second.Summary);
        Assert.True(aged.IsActive);
        Assert.Equal(0, aged.MissedRuns);
        Assert.Equal(0, first.Summary.MarkedInactive);
        Assert.Equal(1, second.Summary.MarkedInactive);
        Assert.False(recent.IsActive);
        Assert.Equal(2, recent.MissedRuns);
    }

    [Fact]
    public async Task RunAsync_WhenATrickleSourceStopsListingAJob_KeepsTheJobActive()
    {
        FakeJobSource source = new(JobSourceKind.RemoteOk);
        source.Returns(
            TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-1", FirstUrl),
            TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-2", SecondUrl, company: "Contoso", title: "Platform Engineer"));
        source.Returns(TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-2", SecondUrl, company: "Contoso", title: "Platform Engineer"));
        source.Returns(TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-2", SecondUrl, company: "Contoso", title: "Platform Engineer"));
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();

        await harness.RunAsync();
        await harness.RunAsync();
        RefreshResult third = await harness.RunAsync();

        Job job = await harness.JobByPostingUrlAsync(FirstUrl);
        Assert.NotNull(third.Summary);
        Assert.Equal(0, third.Summary.MarkedInactive);
        Assert.True(job.IsActive);
        Assert.Equal(0, job.MissedRuns);
    }

    [Fact]
    public async Task RunAsync_ForAnInboxJobOlderThanTheAgeLimit_DropsItAsStale()
    {
        FakeJobSource source = new(JobSourceKind.RemoteOk);
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();
        await harness.SaveAsync(AgedInboxJob(DateTimeOffset.UtcNow.AddDays(-60)));

        RefreshResult result = await harness.RunAsync();

        Job job = await harness.SingleJobAsync();
        Assert.NotNull(result.Summary);
        Assert.Equal(1, result.Summary.MarkedStale);
        Assert.Equal(PrefilterState.Dropped, job.Prefilter);
        Assert.Equal(RefreshService.StaleReason, job.DropReason);
        Assert.Equal(JobClass.D, job.Class);
        Assert.Equal(0, result.Summary.Scored);
    }

    [Fact]
    public async Task RunAsync_ForAnAgedManualJob_KeepsItInTheInbox()
    {
        FakeJobSource source = new(JobSourceKind.RemoteOk);
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();
        await harness.SaveAsync(ManualInboxJob(DateTimeOffset.UtcNow.AddDays(-60)));

        RefreshResult result = await harness.RunAsync();

        Job job = await harness.SingleJobAsync();
        Assert.NotNull(result.Summary);
        Assert.Equal(0, result.Summary.MarkedStale);
        Assert.Equal(PrefilterState.Passed, job.Prefilter);
        Assert.Null(job.DropReason);
        Assert.True(job.IsActive);
        Assert.Equal(TriageState.New, job.Triage);
    }

    [Fact]
    public async Task RunAsync_WithAManualJobAmongMoreCandidatesThanTheCap_ScoresTheManualJobFirst()
    {
        FakeJobSource source = new(JobSourceKind.RemoteOk);
        source.Returns(
            TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-1", "https://jobs.example.com/1", company: "Northwind"),
            TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-2", "https://jobs.example.com/2", company: "Contoso"),
            TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-3", "https://jobs.example.com/3", company: "Fabrikam"),
            TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-4", "https://jobs.example.com/4", company: "Tailspin"),
            TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-5", "https://jobs.example.com/5", company: "Fourth Coffee"));
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();
        await harness.SaveAsync(ManualInboxJob(DateTimeOffset.UtcNow.AddDays(-3)));
        await harness.Settings.ApplyAsync(settings => settings.ConfigureRunLimits(21, 21, 12, 2));

        RefreshResult result = await harness.RunAsync();

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
        source.Returns(
            TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-1", "https://jobs.example.com/1", company: "Northwind"),
            TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-2", "https://jobs.example.com/2", company: "Contoso"),
            TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-3", "https://jobs.example.com/3", company: "Fabrikam"),
            TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-4", "https://jobs.example.com/4", company: "Tailspin"),
            TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-5", "https://jobs.example.com/5", company: "Fourth Coffee"));
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();
        await harness.Settings.ApplyAsync(settings => settings.ConfigureRunLimits(21, 21, 12, 2));

        RefreshResult result = await harness.RunAsync();

        List<Job> jobs = await harness.JobsAsync();
        Assert.NotNull(result.Summary);
        Assert.Equal(5, jobs.Count);
        Assert.Equal(2, result.Summary.Scored);
        Assert.Equal(2, harness.Scorer.Requests.Count);
        Assert.Equal(2, jobs.Count(job => job.Scoring == ScoringState.Scored));
    }

    [Fact]
    public async Task RunAsync_WithoutAnApiKey_LeavesTheJobsUnscored()
    {
        FakeJobSource source = new(JobSourceKind.RemoteOk);
        source.Returns(TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-1", FirstUrl));
        await using RefreshTestHarness harness = new(apiKeyPresent: false, source);
        await harness.InitializeAsync();

        RefreshResult result = await harness.RunAsync();

        Job job = await harness.SingleJobAsync();
        Assert.NotNull(result.Summary);
        Assert.Equal(0, result.Summary.Scored);
        Assert.Empty(harness.Scorer.Requests);
        Assert.Equal(ScoringState.Unscored, job.Scoring);
        Assert.Equal(1, await harness.Refresher.CountJobsAwaitingScoreAsync());
    }

    [Fact]
    public async Task RunAsync_WhenAScoringCallFails_RecordsTheFailureAndLeavesTheOtherJobScored()
    {
        FakeJobSource source = new(JobSourceKind.RemoteOk);
        source.Returns(
            TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-1", "https://jobs.example.com/1", company: "Northwind"),
            TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-2", "https://jobs.example.com/2", company: "Contoso"));
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();
        harness.Scorer.Answer = request => request.Company == "Contoso"
            ? ScoreOutcome.Failure("the model refused", retryable: false)
            : ScoreOutcome.Success(FakeJobScorer.StrongPayload(request.JobId), FakeJobScorer.ModelName, LlmUsage.None);

        RefreshResult result = await harness.RunAsync();

        List<Job> jobs = await harness.JobsAsync();
        Assert.NotNull(result.Summary);
        Assert.Equal(1, result.Summary.Scored);
        Assert.Equal(1, result.Summary.ScoreFailures);
        Assert.Equal("the model refused", jobs.Single(job => job.Company == "Contoso").ScoreError);
        Assert.Equal(JobClass.A, jobs.Single(job => job.Company == "Northwind").Class);
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

        RefreshResult result = await harness.RunAsync();

        FetchRun stored = Assert.Single(await harness.RunsAsync());
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

        RefreshResult result = await harness.RunAsync();

        int sent = harness.Scorer.Requests.Count;
        List<Job> jobs = await harness.JobsAsync();
        FetchRun stored = Assert.Single(await harness.RunsAsync());
        string limitReason = FakeJobScorer.UsageLimitOutcome().FailureReason!;
        Assert.InRange(sent, 1, RefreshService.ScoringConcurrency);
        Assert.Equal(sent, jobs.Count(job => job.Scoring == ScoringState.Failed && job.ScoreError == limitReason));
        Assert.Equal(jobCount - sent, jobs.Count(job => job.Scoring == ScoringState.Unscored && job.ScoreError is null));
        Assert.Equal(limitReason, stored.ScoringHaltReason);
        Assert.Equal(new ScoringFailureReason(limitReason, sent), Assert.Single(stored.ScoringFailureReasons));
        Assert.Equal(sent, stored.ScoreFailures);
        Assert.Equal(FetchOutcome.Completed, stored.Outcome);
        Assert.NotNull(result.Summary);
        Assert.Equal(limitReason, result.Summary.ScoringHaltReason);
        Assert.Equal(jobCount, await harness.Refresher.CountJobsAwaitingScoreAsync());
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
            if (Interlocked.Increment(ref arrived) == RefreshService.ScoringConcurrency)
            {
                allInFlight.TrySetResult();
            }

            await allInFlight.Task.WaitAsync(TimeSpan.FromSeconds(10));

            return FakeJobScorer.UsageLimitOutcome();
        };

        await harness.RunAsync();

        List<Job> jobs = await harness.JobsAsync();
        Assert.Equal(RefreshService.ScoringConcurrency, harness.Scorer.Requests.Count);
        Assert.Equal(RefreshService.ScoringConcurrency, jobs.Count(job => job.Scoring == ScoringState.Failed));
        Assert.Equal(jobCount - RefreshService.ScoringConcurrency, jobs.Count(job => job.Scoring == ScoringState.Unscored));
    }

    [Fact]
    public async Task RunAsync_AfterAUsageLimitStop_ScoresTheJobsLeftBehindOnTheNextRun()
    {
        const int jobCount = 6;
        FakeJobSource source = new(JobSourceKind.RemoteOk);
        source.Returns(DistinctPostings(jobCount));
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();
        harness.Scorer.Answer = _ => FakeJobScorer.UsageLimitOutcome();
        await harness.RunAsync();
        harness.Scorer.Answer = null;

        RefreshResult second = await harness.RunAsync();

        Assert.NotNull(second.Summary);
        Assert.Equal(jobCount, second.Summary.Scored);
        Assert.Null(second.Summary.ScoringHaltReason);
        Assert.All(await harness.JobsAsync(), job => Assert.Equal(ScoringState.Scored, job.Scoring));
    }

    [Fact]
    public async Task RunAsync_WhenASourceThrows_RecordsAnErrorLineAndKeepsTheRunGoing()
    {
        FakeJobSource failing = new(JobSourceKind.RemoteOk) { ExceptionToThrow = new HttpRequestException("the feed answered 503") };
        FakeJobSource working = new(JobSourceKind.WeWorkRemotely);
        working.Returns(TestPostings.Posting(JobSourceKind.WeWorkRemotely, "wwr-1", FirstUrl));
        await using RefreshTestHarness harness = new(failing, working);
        await harness.InitializeAsync();

        RefreshResult result = await harness.RunAsync();

        Assert.NotNull(result.Summary);
        Assert.Equal(FetchOutcome.Completed, result.Summary.Outcome);
        Assert.Equal("the feed answered 503", result.Summary.SourceResults.Single(line => line.Kind == JobSourceKind.RemoteOk).Error);
        Assert.Equal(1, result.Summary.SourceResults.Single(line => line.Kind == JobSourceKind.WeWorkRemotely).New);
        Assert.Contains("RemoteOk: the feed answered 503", result.Summary.ErrorLines);
        Assert.Single(await harness.JobsAsync());
    }

    [Fact]
    public async Task RunAsync_WhileARunIsInProgress_RefusesTheSecondCall()
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

        Task<RefreshResult> first = harness.RunAsync();
        await fetching.Task;
        RefreshResult second = await harness.RunAsync();
        release.TrySetResult();
        RefreshResult completed = await first;

        Assert.Equal(RefreshService.AlreadyRunningMessage, second.Refusal);
        Assert.False(second.Started);
        Assert.True(completed.Started);
        Assert.Single(await harness.RunsAsync());
    }

    [Fact]
    public async Task RunAsync_OnTheFirstRun_AsksEverySourceForTheWholeIntakeWindowAndADatedCacheFolder()
    {
        FakeJobSource source = new(JobSourceKind.RemoteOk);
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();

        await harness.RunAsync();

        SourceFetchContext firstContext = source.Contexts[0];
        string expectedFolder = Path.Combine(harness.DataFolder, "raw", "remoteok", DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"));
        List<FetchRun> runs = await harness.RunsAsync();

        Assert.Equal(Path.GetFullPath(expectedFolder), Path.GetFullPath(firstContext.RawCacheFolder));
        Assert.Equal(runs[0].StartedAt.AddDays(-21), firstContext.NotBefore);
    }

    [Fact]
    public async Task RunAsync_OnALaterRun_AsksEverySourceForTheWholeIntakeWindow()
    {
        FakeJobSource source = new(JobSourceKind.RemoteOk);
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();

        await harness.RunAsync();
        await harness.RunAsync();

        SourceFetchContext secondContext = source.Contexts[1];
        List<FetchRun> runs = await harness.RunsAsync();

        Assert.Equal(runs[1].StartedAt.AddDays(-21), secondContext.NotBefore);
        Assert.NotEqual(runs[0].StartedAt.AddDays(-1), secondContext.NotBefore);
    }

    [Theory]
    [InlineData(JobSourceKind.RemoteOk)]
    [InlineData(JobSourceKind.WeWorkRemotely)]
    public async Task RunAsync_ForABoardPostingOlderThanTheIntakeStart_DoesNotCreateAJob(JobSourceKind kind)
    {
        FakeJobSource source = new(kind);
        source.Returns(TestPostings.Posting(kind, "board-old", FirstUrl, postedAt: DateTimeOffset.UtcNow.AddDays(-40)));
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();

        RefreshResult result = await harness.RunAsync();

        Assert.Empty(await harness.JobsAsync());
        Assert.NotNull(result.Summary);
        Assert.Equal(0, result.Summary.NewJobs);
    }

    [Fact]
    public async Task RunAsync_ForABoardPostingInsideTheIntakeWindow_CreatesAJob()
    {
        FakeJobSource source = new(JobSourceKind.RemoteOk);
        source.Returns(TestPostings.Posting(JobSourceKind.RemoteOk, "board-recent", FirstUrl, postedAt: DateTimeOffset.UtcNow.AddDays(-5)));
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();

        RefreshResult result = await harness.RunAsync();

        Assert.Single(await harness.JobsAsync());
        Assert.NotNull(result.Summary);
        Assert.Equal(1, result.Summary.NewJobs);
    }

    [Fact]
    public async Task RunAsync_ForAnUndatedBoardPosting_CreatesAJob()
    {
        FakeJobSource source = new(JobSourceKind.WeWorkRemotely);
        source.Returns(TestPostings.Posting(JobSourceKind.WeWorkRemotely, "board-undated", FirstUrl) with { PostedAt = null });
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();

        await harness.RunAsync();

        Job job = await harness.SingleJobAsync();
        Assert.Null(job.PostedAt);
    }

    [Fact]
    public async Task RunAsync_ForAKnownJobWhoseBoardPostingIsOlderThanTheIntakeStart_StillRecordsTheSighting()
    {
        DateTimeOffset postedAt = DateTimeOffset.UtcNow.AddDays(-40);
        DateTimeOffset seenAt = DateTimeOffset.UtcNow.AddDays(-10);
        string canonicalUrl = UrlCanonicalizer.Canonicalize(FirstUrl);
        Job known = Job.Create(JobFingerprint.ForCanonicalUrl(canonicalUrl), canonicalUrl, FirstUrl, "Northwind", "Senior Backend Engineer", "Plain text description.", "known-hash", seenAt, isManual: false);
        known.RecordSource(JobSourceKind.RemoteOk, "board-known", seenAt);
        known.RecordPostingFacts(null, null, null, [], null, postedAt);
        known.ApplyPrefilterVerdict(PrefilterState.Passed, null, []);
        FakeJobSource source = new(JobSourceKind.RemoteOk);
        source.Returns(TestPostings.Posting(JobSourceKind.RemoteOk, "board-known", FirstUrl, postedAt: postedAt));
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();
        await harness.SaveAsync(known);

        RefreshResult result = await harness.RunAsync();

        Job job = await harness.SingleJobAsync();
        Assert.NotNull(result.Summary);
        Assert.Equal(0, result.Summary.NewJobs);
        Assert.Equal(1, result.Summary.UpdatedJobs);
        Assert.True(job.LastSeenAt > seenAt.AddDays(9));
        Assert.True(Assert.Single(job.Sources).LastSeenAt > seenAt.AddDays(9));
    }

    [Fact]
    public async Task IsStartupRefreshDueAsync_WithNoCompletedRun_ReturnsTrue()
    {
        await using RefreshTestHarness harness = new();
        await harness.InitializeAsync();

        Assert.True(await harness.Refresher.IsStartupRefreshDueAsync());
    }

    [Fact]
    public async Task IsStartupRefreshDueAsync_WithARecentCompletedRun_ReturnsFalse()
    {
        await using RefreshTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(CompletedRun(DateTimeOffset.UtcNow.AddHours(-2)));

        Assert.False(await harness.Refresher.IsStartupRefreshDueAsync());
    }

    [Fact]
    public async Task IsStartupRefreshDueAsync_WithACompletedRunOlderThanTheSetting_ReturnsTrue()
    {
        await using RefreshTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(CompletedRun(DateTimeOffset.UtcNow.AddHours(-13)));

        Assert.True(await harness.Refresher.IsStartupRefreshDueAsync());
    }

    [Fact]
    public async Task IsStartupRefreshDueAsync_WithTheIntervalAtZeroAndNoCompletedRun_ReturnsFalse()
    {
        await using RefreshTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.Settings.ApplyAsync(settings => settings.ConfigureRunLimits(21, 21, 0, 300));

        Assert.False(await harness.Refresher.IsStartupRefreshDueAsync());
    }

    [Fact]
    public async Task IsStartupRefreshDueAsync_WithTheIntervalAtZeroAndAnOldCompletedRun_ReturnsFalse()
    {
        await using RefreshTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.Settings.ApplyAsync(settings => settings.ConfigureRunLimits(21, 21, 0, 300));
        await harness.SaveAsync(CompletedRun(DateTimeOffset.UtcNow.AddDays(-30)));

        Assert.False(await harness.Refresher.IsStartupRefreshDueAsync());
    }

    [Fact]
    public async Task RunAsync_OnASecondRunOverTheSamePostings_ReportsNothingNewAndNothingScored()
    {
        FakeJobSource source = new(JobSourceKind.RemoteOk);
        source.Returns(TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-1", FirstUrl));
        source.Returns(TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-1", FirstUrl));
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();

        RefreshResult first = await harness.RunAsync();
        RefreshResult second = await harness.RunAsync();

        Assert.NotNull(first.Summary);
        Assert.NotNull(second.Summary);
        Assert.Equal(1, first.Summary.NewJobs);
        Assert.Equal(1, first.Summary.Scored);
        Assert.Equal(0, second.Summary.NewJobs);
        Assert.Equal(0, second.Summary.Scored);
        Assert.Single(harness.Scorer.Requests);
    }

    [Fact]
    public async Task RunAsync_WhileItRuns_PublishesThePhaseAndTheSourceCountersOnTheSharedState()
    {
        FakeJobSource source = new(JobSourceKind.RemoteOk);
        source.Returns(TestPostings.Posting(JobSourceKind.RemoteOk, "remoteok-1", FirstUrl));
        await using RefreshTestHarness harness = new(source);
        await harness.InitializeAsync();
        Lock phaseGate = new();
        List<RefreshPhase> phases = [];
        harness.State.Changed += () =>
        {
            lock (phaseGate)
            {
                phases.Add(harness.State.Phase);
            }
        };

        await harness.RunAsync();

        Assert.False(harness.State.IsRunning);
        Assert.Contains(RefreshPhase.Fetching, phases);
        Assert.Contains(RefreshPhase.Scoring, phases);
        Assert.NotNull(harness.State.LastRun);
        Assert.Equal(1, harness.State.LastRun.NewJobs);
        Assert.Equal(JobSourceKind.RemoteOk, Assert.Single(harness.State.LastRun.SourceResults).Kind);
    }

    [Fact]
    public async Task RestoreLastRunAsync_WithAFinishedRunInTheDatabase_PutsItsSummaryOnTheState()
    {
        await using RefreshTestHarness harness = new();
        await harness.InitializeAsync();
        DateTimeOffset at = DateTimeOffset.UtcNow.AddHours(-2);
        FetchRun stored = FetchRun.Start(FetchTrigger.Startup, at);
        stored.RecordSourceResult(JobSourceKind.RemoteOk, 12, 3, 2, 1, null);
        stored.RecordScoring(3, ["the model refused"]);
        stored.RecordLiveness(2, 4);
        stored.Complete(at.AddMinutes(1));
        await harness.SaveAsync(stored);

        await harness.Refresher.RestoreLastRunAsync();

        RefreshRunSummary? summary = harness.State.LastRun;
        Assert.NotNull(summary);
        Assert.Equal(stored.Id, summary.RunId);
        Assert.Equal(FetchTrigger.Startup, summary.Trigger);
        Assert.Equal(3, summary.Scored);
        Assert.Equal(1, summary.ScoreFailures);
        Assert.Equal(2, summary.MarkedInactive);
        Assert.Equal(4, summary.MarkedStale);
        Assert.Equal(JobSourceKind.RemoteOk, Assert.Single(summary.SourceResults).Kind);
    }

    [Fact]
    public async Task RestoreLastRunAsync_WithOnlyAnUnfinishedRun_LeavesThePanelEmpty()
    {
        await using RefreshTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(FetchRun.Start(FetchTrigger.Manual, DateTimeOffset.UtcNow.AddHours(-3)));

        await harness.Refresher.RestoreLastRunAsync();

        Assert.Null(harness.State.LastRun);
    }

    /// <summary>Postings of different companies, so that no two of them merge as duplicates.</summary>
    private static RawJob[] DistinctPostings(int count)
    {
        return [.. DistinctCompanies.Take(count).Select((company, index) => TestPostings.Posting(JobSourceKind.RemoteOk, $"remoteok-{index}", $"https://jobs.example.com/{index}", company: company))];
    }

    private static FetchRun CompletedRun(DateTimeOffset at)
    {
        FetchRun run = FetchRun.Start(FetchTrigger.Manual, at);
        run.Complete(at.AddMinutes(1));

        return run;
    }

    private static Job AgedInboxJob(DateTimeOffset firstSeenAt)
    {
        Job job = Job.Create("aged-fingerprint", "https://jobs.example.com/aged", "https://jobs.example.com/aged", "Oldco", "Senior Backend Engineer", "Plain text description.", "aged-hash", firstSeenAt, isManual: false);
        job.RecordSource(JobSourceKind.RemoteOk, "remoteok-aged", firstSeenAt);
        job.ApplyPrefilterVerdict(PrefilterState.Passed, null, []);

        return job;
    }

    /// <summary>A job the dataset listed once, carrying the posting date that decides whether the liveness pass still judges it.</summary>
    private static Job SnapshotJob(string fingerprint, string postingUrl, string company, string title, DateTimeOffset postedAt, DateTimeOffset seenAt)
    {
        Job job = Job.Create(fingerprint, postingUrl, postingUrl, company, title, "Plain text description.", $"{fingerprint}-hash", seenAt, isManual: false);
        job.RecordSource(JobSourceKind.Dataset, $"greenhouse:{fingerprint}", seenAt);
        job.RecordPostingFacts(null, null, AtsKind.Greenhouse, [], null, postedAt);
        job.ApplyPrefilterVerdict(PrefilterState.Passed, null, []);

        return job;
    }

    /// <summary>A job typed in by hand: no posting date, no feed behind it, and outside the drop rules.</summary>
    private static Job ManualInboxJob(DateTimeOffset firstSeenAt)
    {
        Job job = Job.Create("manual-fingerprint", "https://jobs.example.com/manual", "https://jobs.example.com/manual", "Handco", "Staff Platform Engineer", "Plain text description.", "manual-hash", firstSeenAt, isManual: true);
        job.RecordSource(JobSourceKind.Manual, "manual-1", firstSeenAt);
        job.ApplyPrefilterVerdict(PrefilterState.Passed, null, []);

        return job;
    }
}
