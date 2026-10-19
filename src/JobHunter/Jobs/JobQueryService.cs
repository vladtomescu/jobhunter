using System.Text.RegularExpressions;
using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Pipeline;
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

    /// <summary>The flags a job must carry all of; none leaves the list unnarrowed by flag.</summary>
    public IReadOnlyCollection<JobFlag> Flags { get; set; } = [];

    /// <summary>The earliest a listed job was posted, or first seen when the posting gives no date, the same age the rows show; null leaves the list unnarrowed by age.</summary>
    public DateTimeOffset? PostedSince { get; set; }

    /// <summary>Whether the listed jobs state their pay; any leaves the list unnarrowed by pay.</summary>
    public InboxPayFilter Pay { get; set; }

    /// <summary>Where the listed jobs are done from, as the scorer read the posting; any leaves the list unnarrowed by place.</summary>
    public InboxPlaceFilter Place { get; set; }

    /// <summary>The zero-based page to read; a page past the last one reads the last.</summary>
    public int PageIndex { get; set; }
}

/// <summary>The inbox narrowed by stated pay.</summary>
public enum InboxPayFilter
{
    /// <summary>Every job, with pay or without.</summary>
    Any,

    /// <summary>The jobs with a stated pay figure.</summary>
    Stated,

    /// <summary>The jobs with no pay figure.</summary>
    NotStated
}

/// <summary>The inbox narrowed by where the job is done from.</summary>
public enum InboxPlaceFilter
{
    /// <summary>Every job, wherever it is done from.</summary>
    Any,

    /// <summary>The jobs the scorer read as remote.</summary>
    Remote,

    /// <summary>The onsite or hybrid jobs whose posting offers relocation, a visa or sponsorship.</summary>
    OnsiteWithRelocation,

    /// <summary>Every job that is neither remote nor onsite or hybrid with relocation offered.</summary>
    Rest
}

/// <summary>The columns the full list can be ordered by.</summary>
public enum JobListColumn
{
    /// <summary>When the job was posted, or first seen when the posting gives no date, the same age the column shows.</summary>
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

    /// <summary>The flags a job must carry all of; none leaves the list unnarrowed by flag.</summary>
    public IReadOnlyCollection<JobFlag> Flags { get; set; } = [];

    public TriageState? Triage { get; set; }

    public PrefilterState? Prefilter { get; set; }

    public ScoringState? Scoring { get; set; }

    public string? Text { get; set; }

    /// <summary>The earliest a listed job was posted, or first seen when the posting gives no date, the same age the list shows; null leaves the list unnarrowed by age.</summary>
    public DateTimeOffset? PostedSince { get; set; }

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
    private const string RemotePolicyRemote = "remote";
    private const string RemotePolicyHybrid = "hybrid";
    private const string RemotePolicyOnsite = "onsite";
    private const string RelocationSupportCaseless = "(?i)" + GeographyRules.RelocationSupportPattern;

    /// <summary>One page of the jobs waiting for a decision: active, passed, still new, classed A or B and without an application, ordered class first, then highest score, then newest posting, then pay.</summary>
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

        inbox = CarryingAll(inbox, filter.Flags);
        inbox = PostedSince(inbox, filter.PostedSince);
        inbox = WithPay(inbox, filter.Pay);
        inbox = InPlace(inbox, filter.Place);

        Dictionary<JobClass, int> classCounts = (await inbox
            .GroupBy(job => job.Class!.Value)
            .Select(group => new { Class = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken))
            .ToDictionary(group => group.Class, group => group.Count);

        int pageIndex = Paging.ClampPageIndex(filter.PageIndex, classCounts.Values.Sum());

        List<InboxRow> rows = await inbox
            .OrderBy(job => job.Class)
            .ThenByDescending(job => job.Score!.Total)
            .ThenByDescending(job => job.PostedAt ?? job.FirstSeenAt)
            .ThenBy(job => (job.CompMaxPerYear ?? job.CompMinPerYear) == null ? 1 : 0)
            .ThenByDescending(job => job.CompMaxPerYear ?? job.CompMinPerYear)
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

        jobs = CarryingAll(jobs, filter.Flags);
        jobs = PostedSince(jobs, filter.PostedSince);

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

    /// <summary>The skill catalog as stored, to resolve the skills on a score card to their canonical skills and CV marks.</summary>
    public async Task<SkillCatalog> GetSkillCatalogAsync(CancellationToken cancellationToken = default)
    {
        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return new SkillCatalog(await context.Skills.AsNoTracking().ToListAsync(cancellationToken));
    }

    /// <summary>Narrows the jobs to the ones carrying every one of the flags, so each flag asked for shortens the list; no flags leave it whole.</summary>
    private static IQueryable<Job> CarryingAll(IQueryable<Job> jobs, IReadOnlyCollection<JobFlag> flags)
    {
        foreach (JobFlag flag in flags)
        {
            jobs = jobs.Where(job => job.Flags.Contains(flag));
        }

        return jobs;
    }

    /// <summary>Narrows the jobs to the ones with a pay figure, or to the ones without; any leaves them whole.</summary>
    private static IQueryable<Job> WithPay(IQueryable<Job> jobs, InboxPayFilter pay)
    {
        return pay switch
        {
            InboxPayFilter.Stated => jobs.Where(job => job.CompMinPerYear != null || job.CompMaxPerYear != null),
            InboxPayFilter.NotStated => jobs.Where(job => job.CompMinPerYear == null && job.CompMaxPerYear == null),
            _ => jobs
        };
    }

    /// <summary>Narrows the jobs by the remote policy the scorer read, and for onsite or hybrid jobs by whether the posting offers relocation, with the words the geography rules use; any leaves them whole.</summary>
    private static IQueryable<Job> InPlace(IQueryable<Job> jobs, InboxPlaceFilter place)
    {
        return place switch
        {
            InboxPlaceFilter.Remote => jobs.Where(job => job.Score!.RemotePolicy == RemotePolicyRemote),
            InboxPlaceFilter.OnsiteWithRelocation => jobs.Where(job => (job.Score!.RemotePolicy == RemotePolicyOnsite || job.Score!.RemotePolicy == RemotePolicyHybrid)
                && Regex.IsMatch(job.DescriptionText, RelocationSupportCaseless)),
            InboxPlaceFilter.Rest => jobs.Where(job => job.Score!.RemotePolicy != RemotePolicyRemote
                && !((job.Score!.RemotePolicy == RemotePolicyOnsite || job.Score!.RemotePolicy == RemotePolicyHybrid) && Regex.IsMatch(job.DescriptionText, RelocationSupportCaseless))),
            _ => jobs
        };
    }

    /// <summary>Narrows the jobs to the ones posted, or first seen when the posting gives no date, at or after the moment; none leaves them whole.</summary>
    private static IQueryable<Job> PostedSince(IQueryable<Job> jobs, DateTimeOffset? since)
    {
        return since is DateTimeOffset cutoff ? jobs.Where(job => (job.PostedAt ?? job.FirstSeenAt) >= cutoff) : jobs;
    }

    private static IOrderedQueryable<Job> OrderJobs(IQueryable<Job> jobs, JobListFilter filter)
    {
        return (filter.OrderBy, filter.Descending) switch
        {
            (JobListColumn.Company, false) => jobs.OrderBy(job => EF.Functions.Collate(job.Company, NoCase)),
            (JobListColumn.Company, true) => jobs.OrderByDescending(job => EF.Functions.Collate(job.Company, NoCase)),
            (JobListColumn.Title, false) => jobs.OrderBy(job => EF.Functions.Collate(job.Title, NoCase)),
            (JobListColumn.Title, true) => jobs.OrderByDescending(job => EF.Functions.Collate(job.Title, NoCase)),
            (_, false) => jobs.OrderBy(job => job.PostedAt ?? job.FirstSeenAt),
            _ => jobs.OrderByDescending(job => job.PostedAt ?? job.FirstSeenAt),
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
