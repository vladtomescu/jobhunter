using System.Globalization;
using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Pipeline;
using JobHunter.Settings;
using JobHunter.Sources;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Refresh;

/// <summary>What one call to run a refresh or a score run produced: the summary of the run, or the reason the call was refused.</summary>
public sealed record RefreshResult(RefreshRunSummary? Summary, string? Refusal)
{
    /// <summary>A run that went through, with what it changed.</summary>
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

    /// <summary>True when the call actually ran.</summary>
    public bool Started => Summary is not null;
}

/// <summary>Runs one refresh from end to end: fetch every enabled source, merge what they return into the known jobs, prefilter, then check liveness and aging; it sends nothing to the model, which is the score run's job.</summary>
/// <remarks>The settings type is written qualified because the JobHunter.Settings namespace shadows the plain name.</remarks>
public sealed class RefreshService(
    IServiceScopeFactory scopeFactory,
    IDbContextFactory<JobHunterDbContext> contextFactory,
    SettingsService settingsService,
    Prefilter prefilter,
    CompNormalizer compNormalizer,
    DataPaths dataPaths,
    RunGate runGate,
    ScoreBacklog backlog,
    RefreshState state,
    ILogger<RefreshService> logger)
{
    /// <summary>Why a job that sat unanswered in the inbox is dropped.</summary>
    public const string StaleReason = "stale: unanswered in the inbox for more than 45 days";

    /// <summary>Runs one refresh; a call made while a refresh or a score run is in progress is refused with a message instead of being queued.</summary>
    public async Task<RefreshResult> RunAsync(FetchTrigger trigger, CancellationToken cancellationToken = default)
    {
        if (trigger == FetchTrigger.Score)
        {
            throw new ArgumentOutOfRangeException(nameof(trigger), trigger, "A refresh is started by the button or at startup; a score run has its own service.");
        }

        if (!runGate.TryEnter(trigger, out string? refusal))
        {
            return RefreshResult.Refused(refusal);
        }

        try
        {
            return RefreshResult.Ran(await ExecuteAsync(trigger, cancellationToken));
        }
        finally
        {
            runGate.Exit();
        }
    }

    /// <summary>True when no refresh has ever completed, or when the last one finished longer ago than the automatic refresh interval in the settings; a score run does not count, and an interval of zero turns the startup refresh off, so it is never due.</summary>
    public async Task<bool> IsStartupRefreshDueAsync(CancellationToken cancellationToken = default)
    {
        Domain.Settings settings = await settingsService.GetAsync(cancellationToken);

        if (settings.AutoRefreshAfterHours == 0)
        {
            return false;
        }

        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        FetchRun? lastCompleted = await LastCompletedRefreshAsync(context, cancellationToken);

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

    private static Task<FetchRun?> LastCompletedRefreshAsync(JobHunterDbContext context, CancellationToken cancellationToken)
    {
        return context.FetchRuns
            .AsNoTracking()
            .Where(run => run.Outcome == FetchOutcome.Completed && run.Trigger != FetchTrigger.Score)
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

        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        FetchRun run = FetchRun.Start(trigger, startedAt);
        context.FetchRuns.Add(run);
        await context.SaveChangesAsync(cancellationToken);

        // The live state opens only once the run is on record, so a run that cannot be stored never leaves the panel showing a run in progress.
        state.BeginRun(trigger);

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
            run.Complete(DateTimeOffset.UtcNow);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The refresh run failed.");
            await FailedRunRecorder.RecordAsync(context, run, exception, logger);
        }

        await backlog.RecountAsync(CancellationToken.None);

        RefreshRunSummary summary = RefreshRunSummary.FromRun(run);
        state.CompleteRun(summary);

        return summary;
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

    /// <summary>Where a source caches what it downloaded: one folder per source and day under the data folder.</summary>
    private string RawCacheFolder(JobSourceKind kind, DateTimeOffset startedAt)
    {
        return Path.Combine(dataPaths.Raw, kind.ToString().ToLowerInvariant(), startedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    /// <summary>What merging the postings of one source changed.</summary>
    private sealed record MergeTally(int Added, int Updated, int Dropped);
}
