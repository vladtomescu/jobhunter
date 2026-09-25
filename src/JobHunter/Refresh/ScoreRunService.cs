using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Llm;
using JobHunter.Settings;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Refresh;

/// <summary>Runs one score run on demand: sends the jobs waiting for a score to the model, the ones added by hand first and the rest newest first, up to the per-run cap, and records the run in the history.</summary>
/// <remarks>The settings type is written qualified because the JobHunter.Settings namespace shadows the plain name.</remarks>
public sealed class ScoreRunService(
    IDbContextFactory<JobHunterDbContext> contextFactory,
    SettingsService settingsService,
    JobScoringStep scoringStep,
    ApiKeyDetector apiKeyDetector,
    RunGate runGate,
    ScoreBacklog backlog,
    RefreshState state,
    ILogger<ScoreRunService> logger)
{
    /// <summary>How many scoring calls are in flight at once.</summary>
    public const int ScoringConcurrency = 4;

    /// <summary>Runs one score run; a call made while a refresh or another score run is in progress is refused with a message instead of being queued.</summary>
    public async Task<RefreshResult> RunAsync(CancellationToken cancellationToken = default)
    {
        if (!runGate.TryEnter(FetchTrigger.Score, out string? refusal))
        {
            return RefreshResult.Refused(refusal);
        }

        try
        {
            return RefreshResult.Ran(await ExecuteAsync(cancellationToken));
        }
        finally
        {
            runGate.Exit();
        }
    }

    private async Task<RefreshRunSummary> ExecuteAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;

        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        FetchRun run = FetchRun.Start(FetchTrigger.Score, startedAt);
        context.FetchRuns.Add(run);
        await context.SaveChangesAsync(cancellationToken);

        // The live state opens only once the run is on record, so a run that cannot be stored never leaves the panel showing a run in progress.
        state.BeginRun(FetchTrigger.Score);

        try
        {
            Domain.Settings settings = await settingsService.GetAsync(cancellationToken);
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
            logger.LogError(exception, "The score run failed.");
            await FailedRunRecorder.RecordAsync(context, run, exception, logger);
        }

        await backlog.RecountAsync(CancellationToken.None);

        RefreshRunSummary summary = RefreshRunSummary.FromRun(run);
        state.CompleteRun(summary);

        return summary;
    }

    /// <summary>Scores the jobs waiting for a score up to the per-run cap; without a key nothing is sent and the jobs stay unscored.</summary>
    /// <remarks>Once a call is refused because the account reached its usage limit, no further call starts: the calls already in flight finish, and the jobs never sent keep their state so the next score run picks them up.</remarks>
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

        return await ScoreBacklog.Of(context)
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

    /// <summary>What one score run achieved: the jobs scored, the reason of every failed call, and why scoring stopped early when it did.</summary>
    private sealed record ScoringTally(int Scored, IReadOnlyList<string> FailureReasons, string? HaltReason)
    {
        /// <summary>A score run that sent nothing.</summary>
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
