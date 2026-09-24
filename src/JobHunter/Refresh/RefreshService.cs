using System.Globalization;
using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Llm;
using JobHunter.Pipeline;
using JobHunter.Settings;
using JobHunter.Sources;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Refresh;

/// <summary>What one call to run a refresh produced: the summary of the run, or the reason the call was refused.</summary>
public sealed record RefreshResult(RefreshRunSummary? Summary, string? Refusal)
{
    /// <summary>A refresh that went through, with what it changed.</summary>
    public static RefreshResult Ran(RefreshRunSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return new RefreshResult(summary, null);
    }

    /// <summary>A call refused because a run was already in progress; refused calls are never queued.</summary>
    public static RefreshResult Refused(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new RefreshResult(null, reason);
    }

    /// <summary>True when the call actually ran a refresh.</summary>
    public bool Started => Summary is not null;
}

/// <summary>Runs one refresh from end to end: fetch every enabled source, merge what they return into the known jobs, prefilter, check liveness and aging, then score what is left unscored.</summary>
/// <remarks>The settings type is written qualified because the JobHunter.Settings namespace shadows the plain name.</remarks>
public sealed class RefreshService(
    IServiceScopeFactory scopeFactory,
    IDbContextFactory<JobHunterDbContext> contextFactory,
    SettingsService settingsService,
    Prefilter prefilter,
    CompNormalizer compNormalizer,
    JobScoringStep scoringStep,
    ApiKeyDetector apiKeyDetector,
    DataPaths dataPaths,
    RefreshState state,
    ILogger<RefreshService> logger)
{
    /// <summary>What a second refresh is told while the first one is still running.</summary>
    public const string AlreadyRunningMessage = "A refresh is already running; wait for it to finish.";

    /// <summary>Why a job that sat unanswered in the inbox is dropped.</summary>
    public const string StaleReason = "stale: unanswered in the inbox for more than 45 days";

    /// <summary>How many scoring calls are in flight at once.</summary>
    public const int ScoringConcurrency = 4;

    private readonly SemaphoreSlim runGate = new(1, 1);

    /// <summary>Runs one refresh; a call made while another run is in progress is refused with a message instead of being queued.</summary>
    public async Task<RefreshResult> RunAsync(FetchTrigger trigger, CancellationToken cancellationToken = default)
    {
        if (!await runGate.WaitAsync(0, cancellationToken))
        {
            return RefreshResult.Refused(AlreadyRunningMessage);
        }

        try
        {
            return RefreshResult.Ran(await ExecuteAsync(trigger, cancellationToken));
        }
        finally
        {
            runGate.Release();
        }
    }

    /// <summary>True when no run has ever completed, or when the last one finished longer ago than the automatic refresh interval in the settings; an interval of zero turns the startup refresh off, so it is never due.</summary>
    public async Task<bool> IsStartupRefreshDueAsync(CancellationToken cancellationToken = default)
    {
        Domain.Settings settings = await settingsService.GetAsync(cancellationToken);

        if (settings.AutoRefreshAfterHours == 0)
        {
            return false;
        }

        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        FetchRun? lastCompleted = await LastCompletedRunAsync(context, cancellationToken);

        if (lastCompleted is null)
        {
            return true;
        }

        DateTimeOffset finishedAt = lastCompleted.FinishedAt ?? lastCompleted.StartedAt;

        return DateTimeOffset.UtcNow - finishedAt >= TimeSpan.FromHours(settings.AutoRefreshAfterHours);
    }

    /// <summary>Puts the run that finished last on the panel, so that a restart shows what the previous run left behind instead of an empty panel.</summary>
    public async Task RestoreLastRunAsync(CancellationToken cancellationToken = default)
    {
        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        FetchRun? lastFinished = await context.FetchRuns
            .AsNoTracking()
            .Where(run => run.FinishedAt != null)
            .OrderByDescending(run => run.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (lastFinished is not null)
        {
            state.RestoreLastRun(RefreshRunSummary.FromRun(lastFinished));
        }
    }

    /// <summary>How many active jobs passed the prefilter and still carry no score; the panel shows the export hint while this is above zero and no key is configured.</summary>
    public async Task<int> CountJobsAwaitingScoreAsync(CancellationToken cancellationToken = default)
    {
        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await AwaitingScore(context).CountAsync(cancellationToken);
    }

    private static IQueryable<Job> AwaitingScore(JobHunterDbContext context)
    {
        return context.Jobs.Where(job => job.IsActive && job.Prefilter == PrefilterState.Passed && job.Scoring != ScoringState.Scored);
    }

    private static Task<FetchRun?> LastCompletedRunAsync(JobHunterDbContext context, CancellationToken cancellationToken)
    {
        return context.FetchRuns
            .AsNoTracking()
            .Where(run => run.Outcome == FetchOutcome.Completed)
            .OrderByDescending(run => run.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static bool IsEnabled(JobSourceKind kind, Domain.Settings settings)
    {
        return kind switch
        {
            JobSourceKind.RemoteOk => settings.RemoteOkEnabled,
            JobSourceKind.WeWorkRemotely => settings.WwrEnabled,
            JobSourceKind.Dataset => settings.DatasetEnabled,
            _ => false
        };
    }

    private async Task<RefreshRunSummary> ExecuteAsync(FetchTrigger trigger, CancellationToken cancellationToken)
    {
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        state.BeginRun(trigger);

        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        FetchRun run = FetchRun.Start(trigger, startedAt);
        context.FetchRuns.Add(run);
        await context.SaveChangesAsync(cancellationToken);

        try
        {
            Domain.Settings settings = await settingsService.GetAsync(cancellationToken);
            CandidateProfile candidate = CandidateProfile.FromSettings(settings);
            DateTimeOffset intakeStart = ReadIntakeStart(settings, startedAt);
            IReadOnlyList<JobSourceKind> snapshotKinds = await FetchAndMergeAsync(context, run, settings, candidate, startedAt, intakeStart, cancellationToken);

            state.EnterPhase(RefreshPhase.Liveness, "checking which postings are still listed");
            int markedInactive = await MarkMissingJobsInactiveAsync(context, snapshotKinds, startedAt, intakeStart, cancellationToken);
            int markedStale = await DropStaleJobsAsync(context, startedAt, cancellationToken);
            run.RecordLiveness(markedInactive, markedStale);
            await context.SaveChangesAsync(cancellationToken);

            ScoringTally scoring = await ScoreAsync(settings, cancellationToken);
            run.RecordScoring(scoring.Scored, scoring.FailureReasons);

            if (scoring.HaltReason is string haltReason)
            {
                run.HaltScoring(haltReason);
            }

            run.Complete(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The refresh run failed.");
            await RecordFailureAsync(context, run, exception);
        }

        RefreshRunSummary summary = RefreshRunSummary.FromRun(run);
        state.CompleteRun(summary);

        return summary;
    }

    private async Task RecordFailureAsync(JobHunterDbContext context, FetchRun run, Exception exception)
    {
        string message = string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;
        run.Fail(message, DateTimeOffset.UtcNow);

        try
        {
            await context.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception saveFailure)
        {
            logger.LogError(saveFailure, "The failed refresh run could not be stored.");
        }
    }

    /// <summary>Fetches every enabled source and merges what it returns, and reports the full-snapshot sources whose result can carry the liveness pass.</summary>
    private async Task<IReadOnlyList<JobSourceKind>> FetchAndMergeAsync(JobHunterDbContext context, FetchRun run, Domain.Settings settings, CandidateProfile candidate, DateTimeOffset startedAt, DateTimeOffset notBefore, CancellationToken cancellationToken)
    {
        MergeIndex index = await MergeIndex.LoadAsync(context, startedAt, cancellationToken);
        List<JobSourceKind> snapshotKinds = [];

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        List<IJobSource> sources = [.. scope.ServiceProvider.GetServices<IJobSource>().Where(source => IsEnabled(source.Kind, settings))];

        foreach (IJobSource source in sources)
        {
            state.EnterPhase(RefreshPhase.Fetching, $"fetching {source.Kind}");

            SourceFetchContext fetchContext = new(notBefore, candidate, RawCacheFolder(source.Kind, startedAt), settings);
            SourceFetchResult fetched = await FetchAsync(source, fetchContext, cancellationToken);

            state.EnterPhase(RefreshPhase.Merging, $"merging {fetched.Jobs.Count} postings from {source.Kind}");
            MergeTally tally = await MergeAsync(context, index, fetched.Jobs, settings, candidate, startedAt, notBefore, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);

            SourceRunResult result = new(source.Kind, fetched.FetchedCount, tally.Added, tally.Updated, tally.Dropped, fetched.Error);
            run.RecordSourceResult(result.Kind, result.Fetched, result.New, result.Updated, result.Dropped, result.Error);
            state.RecordSourceResult(result);

            if (source.IsFullSnapshot && fetched.Error is null && fetched.Jobs.Count > 0)
            {
                snapshotKinds.Add(source.Kind);
            }
        }

        return snapshotKinds;
    }

    /// <summary>Fetches one source; an exception becomes the error line of an empty result, so the run records it and carries on with the next source.</summary>
    private async Task<SourceFetchResult> FetchAsync(IJobSource source, SourceFetchContext fetchContext, CancellationToken cancellationToken)
    {
        try
        {
            return await source.FetchAsync(fetchContext, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Source {Source} failed.", source.Kind);

            return new SourceFetchResult([], 0, exception.Message);
        }
    }

    /// <summary>Merges the postings of one source into the known jobs: a posting of a known job records the sighting whatever its date, while a posting dated before the intake start never becomes a new job.</summary>
    /// <remarks>The dataset reader already leaves out postings older than the intake start, so the age rule here only ever turns away board postings; it applies to creation alone because a known job the boards still list must keep its sighting, or it would stop being refreshed, and a job deleted as old must not come back as a new row to be scored again.</remarks>
    private async Task<MergeTally> MergeAsync(JobHunterDbContext context, MergeIndex index, IReadOnlyList<RawJob> rawJobs, Domain.Settings settings, CandidateProfile candidate, DateTimeOffset startedAt, DateTimeOffset notBefore, CancellationToken cancellationToken)
    {
        int added = 0;
        int updated = 0;
        int dropped = 0;

        foreach (RawJob raw in rawJobs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(raw.Company) || string.IsNullOrWhiteSpace(raw.Title) || string.IsNullOrWhiteSpace(raw.PostingUrl))
            {
                continue;
            }

            string canonicalUrl = UrlCanonicalizer.Canonicalize(raw.ApplyUrl ?? raw.PostingUrl);
            string fingerprint = JobFingerprint.ForCanonicalUrl(canonicalUrl);
            string description = HtmlToText.Convert(raw.DescriptionRaw);
            string descriptionHash = JobFingerprint.ForDescription(description);

            Job? job = await index.ResolveAsync(context, fingerprint, raw.Company, raw.Title, startedAt, cancellationToken);
            bool revised;

            if (job is null && raw.PostedAt is DateTimeOffset postedAt && postedAt < notBefore)
            {
                continue;
            }

            if (job is null)
            {
                job = Job.Create(fingerprint, canonicalUrl, raw.PostingUrl, raw.Company, raw.Title, description, descriptionHash, startedAt, isManual: false);
                context.Jobs.Add(job);
                index.Add(job);
                added++;
                revised = true;
            }
            else
            {
                revised = job.ReviseDescription(description, descriptionHash);
                updated++;
            }

            job.RecordSource(raw.Source, raw.SourceId, startedAt);
            job.RecordPostingFacts(raw.ApplyUrl, raw.CompanyUrl, AtsKindParser.Parse(raw.Ats, raw.ApplyUrl ?? raw.PostingUrl), raw.Tags, raw.EmploymentType, raw.PostedAt);
            job.RecordPlace(raw.LocationText, raw.CountryIso, raw.RegionText, raw.IsRemote, raw.Language);
            await RecordCompensationAsync(job, raw, settings, cancellationToken);

            if (revised || job.Prefilter == PrefilterState.Pending)
            {
                PrefilterVerdict verdict = prefilter.Evaluate(PrefilterInput.FromJob(job, candidate, settings, startedAt));
                job.ApplyPrefilterVerdict(verdict.State, verdict.DropReason, verdict.Flags, settings.HighPayThresholdPerYear);

                if (verdict.State == PrefilterState.Dropped)
                {
                    dropped++;
                }
            }
        }

        return new MergeTally(added, updated, dropped);
    }

    private async Task RecordCompensationAsync(Job job, RawJob raw, Domain.Settings settings, CancellationToken cancellationToken)
    {
        if (raw.CompMin is null && raw.CompMax is null)
        {
            return;
        }

        YearlyComp comp = await compNormalizer.ToBasePerYearAsync(raw.CompMin, raw.CompMax, raw.CompCurrency, raw.CompPeriod, settings.BaseCurrency, settings, cancellationToken);
        job.RecordCompensation(raw.CompMin, raw.CompMax, raw.CompCurrency, raw.CompPeriod, comp.MinPerYear, comp.MaxPerYear, settings.HighPayThresholdPerYear);
        job.ClearCompensationUnknownFlag();
    }

    /// <summary>The oldest posting date every source is asked for, the intake horizon, which is the whole window setting on every run and not only on the first one.</summary>
    /// <remarks>A posting's date lags the day it reaches the dataset, so a window measured from the previous run returns nothing at all and loses every posting that enters late; the same narrow window would also hide a still-listed older job from the liveness pass and deactivate it.</remarks>
    private static DateTimeOffset ReadIntakeStart(Domain.Settings settings, DateTimeOffset startedAt)
    {
        return startedAt.AddDays(-settings.FirstRunWindowDays);
    }

    /// <summary>Counts a missed run for every active job inside the intake horizon that a full-snapshot source stopped listing, which deactivates it on the second miss.</summary>
    /// <remarks>A job posted before the horizon is outside what the sources are asked for, so its absence proves nothing and it is left to the aging pass instead of being counted as missing.</remarks>
    /// <remarks>One query per snapshot kind, because the source references live in a JSON column where a comparison against a single kind translates to SQL but a membership test over a list does not.</remarks>
    private static async Task<int> MarkMissingJobsInactiveAsync(JobHunterDbContext context, IReadOnlyList<JobSourceKind> snapshotKinds, DateTimeOffset startedAt, DateTimeOffset notBefore, CancellationToken cancellationToken)
    {
        int markedInactive = 0;
        HashSet<Guid> alreadyCounted = [];

        foreach (JobSourceKind kind in snapshotKinds)
        {
            List<Job> unseen = await context.Jobs
                .Where(job => job.IsActive
                    && !job.IsManual
                    && job.LastSeenAt < startedAt
                    && (job.PostedAt == null || job.PostedAt >= notBefore)
                    && job.Sources.Any(reference => reference.Kind == kind))
                .ToListAsync(cancellationToken);

            foreach (Job job in unseen)
            {
                if (alreadyCounted.Add(job.Id) && job.MissRun())
                {
                    markedInactive++;
                }
            }
        }

        return markedInactive;
    }

    /// <summary>Drops the inbox jobs nobody triaged inside the window a posting stays worth answering; a job added by hand is never dropped, because the drop rules do not apply to it.</summary>
    private static async Task<int> DropStaleJobsAsync(JobHunterDbContext context, DateTimeOffset startedAt, CancellationToken cancellationToken)
    {
        DateTimeOffset oldest = startedAt.AddDays(-Prefilter.MaximumAgeDays);

        List<Job> stale = await context.Jobs
            .Where(job => job.IsActive && !job.IsManual && job.Triage == TriageState.New && job.Prefilter == PrefilterState.Passed && job.FirstSeenAt < oldest)
            .ToListAsync(cancellationToken);

        foreach (Job job in stale)
        {
            job.Drop(StaleReason);
        }

        return stale.Count;
    }

    /// <summary>Scores the jobs that passed the prefilter and carry no score, the ones added by hand first and the rest newest first, up to the per-run cap; without a key nothing is sent and the jobs stay unscored.</summary>
    /// <remarks>Once a call is refused because the account reached its usage limit, no further call starts: the calls already in flight finish, and the jobs never sent keep their state so the next run picks them up.</remarks>
    private async Task<ScoringTally> ScoreAsync(Domain.Settings settings, CancellationToken cancellationToken)
    {
        if (!apiKeyDetector.IsPresent)
        {
            state.BeginScoring(0, "no key configured: the jobs stay unscored");

            return ScoringTally.Nothing;
        }

        if (settings.MaxScoresPerRun <= 0)
        {
            state.BeginScoring(0, "scoring is capped at zero jobs per run");

            return ScoringTally.Nothing;
        }

        List<Guid> jobIds = await ReadJobsToScoreAsync(settings, cancellationToken);
        state.BeginScoring(jobIds.Count, jobIds.Count == 0 ? "nothing left to score" : $"scoring {jobIds.Count} jobs");

        if (jobIds.Count == 0)
        {
            return ScoringTally.Nothing;
        }

        using SemaphoreSlim concurrency = new(ScoringConcurrency, ScoringConcurrency);
        ScoreProgress progress = new();

        await Task.WhenAll(jobIds.Select(jobId => ScoreOneAsync(jobId, settings, progress, concurrency, cancellationToken)));

        return new ScoringTally(progress.Scored, progress.FailureReasons, progress.HaltReason);
    }

    /// <summary>Picks what this run scores: jobs added by hand first, because they carry no posting date and would otherwise sit behind every dated posting, then the newest of the rest.</summary>
    private async Task<List<Guid>> ReadJobsToScoreAsync(Domain.Settings settings, CancellationToken cancellationToken)
    {
        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await AwaitingScore(context)
            .AsNoTracking()
            .OrderByDescending(job => job.IsManual)
            .ThenByDescending(job => job.PostedAt ?? job.FirstSeenAt)
            .Take(settings.MaxScoresPerRun)
            .Select(job => job.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Scores one job on its own context through the shared scoring step, so that every score is saved the moment it is applied; once scoring has halted the job is not sent and keeps its state.</summary>
    private async Task ScoreOneAsync(Guid jobId, Domain.Settings settings, ScoreProgress progress, SemaphoreSlim concurrency, CancellationToken cancellationToken)
    {
        await concurrency.WaitAsync(cancellationToken);

        try
        {
            if (progress.HaltReason is not null)
            {
                return;
            }

            await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
            Job? job = await context.Jobs.FirstOrDefaultAsync(candidate => candidate.Id == jobId, cancellationToken);

            if (job is null)
            {
                return;
            }

            JobScoringResult result = await scoringStep.ScoreAsync(job, settings, cancellationToken);

            if (result.FailureReason is not string reason)
            {
                progress.RecordScored();
            }
            else
            {
                progress.RecordFailure(reason);

                if (result.UsageLimitReached && progress.Halt(reason))
                {
                    logger.LogWarning("Scoring stopped for this run: {Reason}", reason);
                    state.EnterPhase(RefreshPhase.Scoring, $"scoring stopped: {reason}");
                }
            }

            await context.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            concurrency.Release();
        }

        state.RecordScoreProgress(progress.Scored, progress.Failures);
    }

    /// <summary>Where a source caches what it downloaded: one folder per source and day under the data folder.</summary>
    private string RawCacheFolder(JobSourceKind kind, DateTimeOffset startedAt)
    {
        return Path.Combine(dataPaths.Raw, kind.ToString().ToLowerInvariant(), startedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    /// <summary>What merging the postings of one source changed.</summary>
    private sealed record MergeTally(int Added, int Updated, int Dropped);

    /// <summary>What the scoring pass of one run achieved: the jobs scored, the reason of every failed call, and why scoring stopped early when it did.</summary>
    private sealed record ScoringTally(int Scored, IReadOnlyList<string> FailureReasons, string? HaltReason)
    {
        /// <summary>A scoring pass that sent nothing.</summary>
        public static ScoringTally Nothing { get; } = new(0, [], null);
    }

    /// <summary>Counts scores and failures across the scoring calls that run side by side, and holds the reason scoring halted once a call reports the account out of allowance.</summary>
    private sealed class ScoreProgress
    {
        private readonly Lock gate = new();

        private readonly List<string> failureReasons = [];

        private int scored;

        private string? haltReason;

        public int Scored => Volatile.Read(ref scored);

        public int Failures
        {
            get
            {
                lock (gate)
                {
                    return failureReasons.Count;
                }
            }
        }

        public IReadOnlyList<string> FailureReasons
        {
            get
            {
                lock (gate)
                {
                    return [.. failureReasons];
                }
            }
        }

        public string? HaltReason => Volatile.Read(ref haltReason);

        public void RecordScored()
        {
            Interlocked.Increment(ref scored);
        }

        public void RecordFailure(string reason)
        {
            lock (gate)
            {
                failureReasons.Add(reason);
            }
        }

        /// <summary>Stops every call that has not started yet; returns true only for the call that stopped scoring first.</summary>
        public bool Halt(string reason)
        {
            return Interlocked.CompareExchange(ref haltReason, reason, null) is null;
        }
    }
}
