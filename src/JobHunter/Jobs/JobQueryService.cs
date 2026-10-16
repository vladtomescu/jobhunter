using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Sources;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Jobs;

/// <summary>One job as the inbox lists it: who and what, where, what it pays, what it scored per dimension and how old it is.</summary>
public sealed record InboxRow(
    Guid Id,
    string Company,
    string Title,
    JobClass Class,
    int Total,
    ScorePoints Points,
    string Reasoning,
    string LevelGuess,
    string RemotePolicy,
    string? LocationText,
    string? CountryIso,
    decimal? CompMinPerYear,
    decimal? CompMaxPerYear,
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
    decimal? CompMinPerYear,
    decimal? CompMaxPerYear,
    string? LocationText,
    string? CountryIso,
    DateTimeOffset? PostedAt,
    DateTimeOffset FirstSeenAt);

/// <summary>One page of the inbox: its rows, the page they sit on and how many jobs wait in each class across all pages.</summary>
public sealed record InboxPage(IReadOnlyList<InboxRow> Rows, int PageIndex, IReadOnlyDictionary<JobClass, int> ClassCounts)
{
    /// <summary>How many jobs match the filters across all pages.</summary>
    public int TotalCount => ClassCounts.Values.Sum();
}

/// <summary>One page of the full list: its rows, the page they sit on and how many jobs match the filters across all pages.</summary>
public sealed record JobListPage(IReadOnlyList<JobListRow> Rows, int PageIndex, int TotalCount);

/// <summary>What the inbox is narrowed by and which page of it is read; every narrowing value left unset widens the list.</summary>
public sealed class InboxFilter
{
    public JobClass? Class { get; set; }

    public JobFlag? Flag { get; set; }

    /// <summary>The zero-based page to read; a page past the last one reads the last.</summary>
    public int PageIndex { get; set; }
}

/// <summary>The columns the full list can be ordered by.</summary>
public enum JobListColumn
{
    /// <summary>When the job was first seen.</summary>
    Age,

    /// <summary>The company, ignoring case.</summary>
    Company,

    /// <summary>The title, ignoring case.</summary>
    Title,
}

/// <summary>What the full job list is narrowed by, how it is ordered and which page of it is read; every narrowing value left unset widens the list.</summary>
public sealed class JobListFilter
{
    public JobClass? Class { get; set; }

    public JobSourceKind? Source { get; set; }

    public JobFlag? Flag { get; set; }

    public TriageState? Triage { get; set; }

    public PrefilterState? Prefilter { get; set; }

    public ScoringState? Scoring { get; set; }

    public string? Text { get; set; }

    /// <summary>The column the list is ordered by; the newest jobs come first unless this says otherwise.</summary>
    public JobListColumn OrderBy { get; set; } = JobListColumn.Age;

    public bool Descending { get; set; } = true;

    /// <summary>The zero-based page to read; a page past the last one reads the last.</summary>
    public int PageIndex { get; set; }

    /// <summary>Orders the list by the column in the direction; a different order returns to the first page, because the page the reader was on holds other rows in the new order.</summary>
    public void SortBy(JobListColumn column, bool descending)
    {
        if (column != OrderBy || descending != Descending)
        {
            PageIndex = 0;
        }

        OrderBy = column;
        Descending = descending;
    }
}

/// <summary>One job with the application opened for it, which is everything the job page shows.</summary>
public sealed record JobDetailView(Job Job, Application? Application);

/// <summary>Reads the job lists the pages show: the inbox, the full list and one job with its application.</summary>
public sealed class JobQueryService(IDbContextFactory<JobHunterDbContext> contextFactory)
{
    private const string LikeEscape = "\\";
    private const string NoCase = "NOCASE";

    /// <summary>One page of the jobs waiting for a decision: active, passed, still new, classed A or B and without an application, ordered class first, then highest score, then pay, then by posting date.</summary>
    public async Task<InboxPage> GetInboxAsync(InboxFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        IQueryable<Job> inbox = context.Jobs
            .AsNoTracking()
            .Where(job => job.IsActive
                && job.Prefilter == PrefilterState.Passed
                && job.Triage == TriageState.New
                && (job.Class == JobClass.A || job.Class == JobClass.B)
                && !context.Applications.Any(application => application.JobId == job.Id));

        if (filter.Class is JobClass jobClass)
        {
            inbox = inbox.Where(job => job.Class == jobClass);
        }

        if (filter.Flag is JobFlag flag)
        {
            inbox = inbox.Where(job => job.Flags.Contains(flag));
        }

        Dictionary<JobClass, int> classCounts = (await inbox
            .GroupBy(job => job.Class!.Value)
            .Select(group => new { Class = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken))
            .ToDictionary(group => group.Class, group => group.Count);

        int pageIndex = Paging.ClampPageIndex(filter.PageIndex, classCounts.Values.Sum());

        List<InboxRow> rows = await inbox
            .OrderBy(job => job.Class)
            .ThenByDescending(job => job.Score!.Total)
            .ThenBy(job => (job.CompMaxPerYear ?? job.CompMinPerYear) == null ? 1 : 0)
            .ThenByDescending(job => job.CompMaxPerYear ?? job.CompMinPerYear)
            .ThenByDescending(job => job.PostedAt ?? job.FirstSeenAt)
            .ThenBy(job => job.Id)
            .Skip(pageIndex * Paging.PageSize)
            .Take(Paging.PageSize)
            .Select(job => new InboxRow(
                job.Id,
                job.Company,
                job.Title,
                job.Class!.Value,
                job.Score!.Total,
                new ScorePoints(job.Score!.Niche, job.Score!.Level, job.Score!.Stack, job.Score!.RemoteTimezone, job.Score!.ContractForm, job.Score!.CompSignal, job.Score!.CompanySignal),
                job.Score!.Reasoning,
                job.Score!.LevelGuess,
                job.Score!.RemotePolicy,
                job.LocationText,
                job.CountryIso,
                job.CompMinPerYear,
                job.CompMaxPerYear,
                job.Flags,
                job.PostedAt,
                job.FirstSeenAt))
            .ToListAsync(cancellationToken);

        return new InboxPage(rows, pageIndex, classCounts);
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

    /// <summary>One page of the full job list narrowed by the filter, newest first unless the filter orders it otherwise.</summary>
    public async Task<JobListPage> GetJobsAsync(JobListFilter filter, CancellationToken cancellationToken = default)
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

        if (filter.Scoring is ScoringState scoring)
        {
            jobs = jobs.Where(job => job.Scoring == scoring);
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

        int totalCount = await jobs.CountAsync(cancellationToken);
        int pageIndex = Paging.ClampPageIndex(filter.PageIndex, totalCount);

        List<JobListRow> rows = await OrderJobs(jobs, filter)
            .ThenBy(job => job.Id)
            .Skip(pageIndex * Paging.PageSize)
            .Take(Paging.PageSize)
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
                job.CompMinPerYear,
                job.CompMaxPerYear,
                job.LocationText,
                job.CountryIso,
                job.PostedAt,
                job.FirstSeenAt))
            .ToListAsync(cancellationToken);

        return new JobListPage(rows, pageIndex, totalCount);
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

    private static IOrderedQueryable<Job> OrderJobs(IQueryable<Job> jobs, JobListFilter filter)
    {
        return (filter.OrderBy, filter.Descending) switch
        {
            (JobListColumn.Company, false) => jobs.OrderBy(job => EF.Functions.Collate(job.Company, NoCase)),
            (JobListColumn.Company, true) => jobs.OrderByDescending(job => EF.Functions.Collate(job.Company, NoCase)),
            (JobListColumn.Title, false) => jobs.OrderBy(job => EF.Functions.Collate(job.Title, NoCase)),
            (JobListColumn.Title, true) => jobs.OrderByDescending(job => EF.Functions.Collate(job.Title, NoCase)),
            (_, false) => jobs.OrderBy(job => job.FirstSeenAt),
            _ => jobs.OrderByDescending(job => job.FirstSeenAt),
        };
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
