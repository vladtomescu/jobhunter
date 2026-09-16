using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Sources;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Jobs;

/// <summary>One job as the inbox lists it: who and what, where, what it pays, what it scored and how old it is.</summary>
public sealed record InboxRow(
    Guid Id,
    string Company,
    string Title,
    JobClass Class,
    int Total,
    string Reasoning,
    string LevelGuess,
    string RemotePolicy,
    string? LocationText,
    string? CountryIso,
    decimal? CompMinEurYear,
    decimal? CompMaxEurYear,
    List<JobFlag> Flags,
    DateTimeOffset? PostedAt,
    DateTimeOffset FirstSeenAt);

/// <summary>One job as the full list shows it, including the ones the rules dropped and the reason they were dropped.</summary>
public sealed record JobListRow(
    Guid Id,
    string Company,
    string Title,
    JobClass? Class,
    int? Total,
    ScoringState Scoring,
    PrefilterState Prefilter,
    string? DropReason,
    TriageState Triage,
    bool IsActive,
    bool IsManual,
    List<JobFlag> Flags,
    List<JobSourceRef> Sources,
    decimal? CompMinEurYear,
    decimal? CompMaxEurYear,
    string? LocationText,
    string? CountryIso,
    DateTimeOffset? PostedAt,
    DateTimeOffset FirstSeenAt);

/// <summary>What the full job list is narrowed by; every value left unset widens the list.</summary>
public sealed class JobListFilter
{
    /// <summary>How many rows one page of the list carries.</summary>
    public const int DefaultTake = 300;

    public JobClass? Class { get; set; }

    public JobSourceKind? Source { get; set; }

    public JobFlag? Flag { get; set; }

    public TriageState? Triage { get; set; }

    public PrefilterState? Prefilter { get; set; }

    public string? Text { get; set; }

    public int Take { get; set; } = DefaultTake;
}

/// <summary>One job with the application opened for it, which is everything the job page shows.</summary>
public sealed record JobDetailView(Job Job, Application? Application);

/// <summary>Reads the job lists the pages show: the inbox, the full list and one job with its application.</summary>
public sealed class JobQueryService(IDbContextFactory<JobHunterDbContext> contextFactory)
{
    private const string LikeEscape = "\\";

    /// <summary>The jobs waiting for a decision: active, passed, still new, classed A or B and without an application, ordered class first, then pay, then by posting date.</summary>
    public async Task<IReadOnlyList<InboxRow>> GetInboxAsync(CancellationToken cancellationToken = default)
    {
        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Jobs
            .AsNoTracking()
            .Where(job => job.IsActive
                && job.Prefilter == PrefilterState.Passed
                && job.Triage == TriageState.New
                && (job.Class == JobClass.A || job.Class == JobClass.B)
                && !context.Applications.Any(application => application.JobId == job.Id))
            .OrderBy(job => job.Class)
            .ThenBy(job => (job.CompMaxEurYear ?? job.CompMinEurYear) == null ? 1 : 0)
            .ThenByDescending(job => job.CompMaxEurYear ?? job.CompMinEurYear)
            .ThenByDescending(job => job.PostedAt ?? job.FirstSeenAt)
            .Select(job => new InboxRow(
                job.Id,
                job.Company,
                job.Title,
                job.Class!.Value,
                job.Score!.Total,
                job.Score!.Reasoning,
                job.Score!.LevelGuess,
                job.Score!.RemotePolicy,
                job.LocationText,
                job.CountryIso,
                job.CompMinEurYear,
                job.CompMaxEurYear,
                job.Flags,
                job.PostedAt,
                job.FirstSeenAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>How many active jobs passed the rules and still carry no score, which is what the export hands to the repository skill.</summary>
    public async Task<int> CountJobsAwaitingScoreAsync(CancellationToken cancellationToken = default)
    {
        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Jobs
            .AsNoTracking()
            .CountAsync(job => job.IsActive && job.Prefilter == PrefilterState.Passed && job.Scoring != ScoringState.Scored, cancellationToken);
    }

    /// <summary>The description of one job, fetched only when a row is expanded so that the lists stay light.</summary>
    public async Task<string> GetDescriptionAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        string? description = await context.Jobs
            .AsNoTracking()
            .Where(job => job.Id == jobId)
            .Select(job => job.DescriptionText)
            .FirstOrDefaultAsync(cancellationToken);

        return description ?? string.Empty;
    }

    /// <summary>The full job list narrowed by the filter, newest first.</summary>
    public async Task<IReadOnlyList<JobListRow>> GetJobsAsync(JobListFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        IQueryable<Job> jobs = context.Jobs.AsNoTracking();

        if (filter.Class is JobClass jobClass)
        {
            jobs = jobs.Where(job => job.Class == jobClass);
        }

        if (filter.Triage is TriageState triage)
        {
            jobs = jobs.Where(job => job.Triage == triage);
        }

        if (filter.Prefilter is PrefilterState prefilter)
        {
            jobs = jobs.Where(job => job.Prefilter == prefilter);
        }

        if (filter.Flag is JobFlag flag)
        {
            jobs = jobs.Where(job => job.Flags.Contains(flag));
        }

        if (filter.Source is JobSourceKind source)
        {
            jobs = jobs.Where(job => job.Sources.Any(reference => reference.Kind == source));
        }

        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            string pattern = LikePattern(filter.Text);
            jobs = jobs.Where(job => EF.Functions.Like(job.Company, pattern, LikeEscape) || EF.Functions.Like(job.Title, pattern, LikeEscape));
        }

        return await jobs
            .OrderByDescending(job => job.FirstSeenAt)
            .Take(filter.Take)
            .Select(job => new JobListRow(
                job.Id,
                job.Company,
                job.Title,
                job.Class,
                job.Score == null ? null : job.Score.Total,
                job.Scoring,
                job.Prefilter,
                job.DropReason,
                job.Triage,
                job.IsActive,
                job.IsManual,
                job.Flags,
                job.Sources,
                job.CompMinEurYear,
                job.CompMaxEurYear,
                job.LocationText,
                job.CountryIso,
                job.PostedAt,
                job.FirstSeenAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>One job with the application opened for it, or null when no job carries that identifier.</summary>
    public async Task<JobDetailView?> GetJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Job? job = await context.Jobs.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == jobId, cancellationToken);
        if (job is null)
        {
            return null;
        }

        Application? application = await context.Applications.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.JobId == jobId, cancellationToken);

        return new JobDetailView(job, application);
    }

    private static string LikePattern(string text)
    {
        string escaped = text.Trim()
            .Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
            .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscape + "_", StringComparison.Ordinal);

        return $"%{escaped}%";
    }
}
