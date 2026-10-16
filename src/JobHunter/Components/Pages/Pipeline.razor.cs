using JobHunter.Applications;
using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Jobs;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Components.Pages;

/// <summary>Code-behind for the pipeline page: applications grouped by status with inline status, next-action and detail editing, plus the ghost-candidate grid.</summary>
/// <remarks>The grouped grid is hand-written table markup because QuickGrid renders exactly one row per item and cannot host the expandable detail row that the contact, notes and comp panel needs; the flat ghost-candidate list is a QuickGrid.</remarks>
/// <remarks>The inline editors are plain inputs: a form component under a cascading edit context that the reload after a save replaces tears the circuit down, so every row edits through its draft and keeps its identity through a key.</remarks>
public sealed partial class Pipeline : ComponentBase
{
    [Inject]
    private IDbContextFactory<JobHunterDbContext> ContextFactory { get; set; } = null!;

    [Inject]
    private ApplicationService ApplicationService { get; set; } = null!;

    [Inject]
    private GhostCandidateQuery GhostCandidateQuery { get; set; } = null!;

    [Inject]
    private JobHunter.Settings.SettingsService SettingsService { get; set; } = null!;

    /// <summary>The status dropdown's order: the progression the enum declares.</summary>
    private static readonly ApplicationStatus[] StatusOrder = Enum.GetValues<ApplicationStatus>();

    /// <summary>The order the groups are listed in: the furthest along first, the closed applications last; the ghost-candidate block sits between Applied and Ghosted.</summary>
    private static readonly ApplicationStatus[] GroupOrder =
    [
        ApplicationStatus.Accepted,
        ApplicationStatus.Offer,
        ApplicationStatus.Fit,
        ApplicationStatus.SystemDesign,
        ApplicationStatus.Tech,
        ApplicationStatus.Manager,
        ApplicationStatus.Screening,
        ApplicationStatus.Saved,
        ApplicationStatus.Applied,
        ApplicationStatus.Ghosted,
        ApplicationStatus.Rejected,
        ApplicationStatus.Withdrawn
    ];

    /// <summary>The group table's headers in column order; the ones with a sort column can be clicked to sort the group.</summary>
    private static readonly HeaderColumn[] HeaderColumns =
    [
        new("Company", PipelineSortColumn.Company),
        new("Title", PipelineSortColumn.Title),
        new("Score", PipelineSortColumn.Score),
        new("Status", null),
        new("Applied", PipelineSortColumn.Applied),
        new("Next action", null),
        new("Due", PipelineSortColumn.Due),
        new(string.Empty, null)
    ];

    /// <summary>The order each group was sorted to by its headers; a group missing here keeps the default order.</summary>
    private readonly Dictionary<ApplicationStatus, GroupSort> sortsByStatus = [];

    private List<Application> applications = [];

    private Dictionary<Guid, JobSummary> jobsById = [];

    private IQueryable<GhostRow> ghostRows = Array.Empty<GhostRow>().AsQueryable();

    private int ghostCandidateCount;

    private readonly HashSet<Guid> expandedApplicationIds = [];

    private readonly Dictionary<Guid, ApplicationDraft> drafts = [];

    private DateOnly today;

    private int ghostThresholdDays;

    private string baseCurrency = string.Empty;

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToLocalTime().DateTime);
        Domain.Settings settings = await SettingsService.GetAsync();
        ghostThresholdDays = settings.GhostThresholdDays;
        baseCurrency = settings.BaseCurrency;

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        await using JobHunterDbContext context = await ContextFactory.CreateDbContextAsync();

        applications = await context.Applications.AsNoTracking().ToListAsync();
        jobsById = await LoadJobSummariesAsync(context, applications.Select(application => application.JobId));

        IReadOnlyList<Application> ghostCandidates = await GhostCandidateQuery.FindAsync(ghostThresholdDays, DateTimeOffset.UtcNow);
        GhostRow[] rows = [.. ghostCandidates.Select(BuildGhostRow)];
        ghostRows = rows.AsQueryable();
        ghostCandidateCount = rows.Length;

        drafts.Clear();
    }

    private static async Task<Dictionary<Guid, JobSummary>> LoadJobSummariesAsync(JobHunterDbContext context, IEnumerable<Guid> jobIds)
    {
        List<Guid> distinctJobIds = [.. jobIds.Distinct()];
        if (distinctJobIds.Count == 0)
        {
            return [];
        }

        List<JobSummary> summaries = await context.Jobs
            .AsNoTracking()
            .Where(job => distinctJobIds.Contains(job.Id))
            .Select(job => new JobSummary(
                job.Id,
                job.Company,
                job.Title,
                job.Score == null ? null : job.Score.Total,
                job.Score == null ? null : new ScorePoints(job.Score.Niche, job.Score.Level, job.Score.Stack, job.Score.RemoteTimezone, job.Score.ContractForm, job.Score.CompSignal, job.Score.CompanySignal),
                job.Class,
                job.CompMinPerYear,
                job.CompMaxPerYear,
                job.Score == null ? null : job.Score.RemotePolicy,
                job.LocationText,
                job.CountryIso,
                job.ApplyUrl ?? job.PostingUrl))
            .ToListAsync();

        return summaries.ToDictionary(summary => summary.JobId);
    }

    private GhostRow BuildGhostRow(Application application)
    {
        JobSummary job = JobFor(application);

        return new GhostRow(
            application.Id,
            job.Company,
            job.Title,
            JobDisplay.StatusLabel(application.Status),
            application.StatusChangedAt.ToLocalTime().ToString("d"));
    }

    private JobSummary JobFor(Application application)
    {
        return jobsById.TryGetValue(application.JobId, out JobSummary? summary) ? summary : JobSummary.Unknown;
    }

    private ApplicationDraft DraftFor(Application application)
    {
        if (!drafts.TryGetValue(application.Id, out ApplicationDraft? draft))
        {
            draft = new ApplicationDraft();
            drafts[application.Id] = draft;
        }

        return draft;
    }

    /// <summary>A group's rows in the order its header asks for; until a header is clicked, the nearest due date first. Ties fall back to the company.</summary>
    private IEnumerable<Application> ApplicationsInStatus(ApplicationStatus status)
    {
        IEnumerable<Application> inStatus = applications.Where(application => application.Status == status);

        IOrderedEnumerable<Application> ordered = !sortsByStatus.TryGetValue(status, out GroupSort? sort)
            ? inStatus.OrderBy(application => application.NextActionDue ?? DateOnly.MaxValue)
            : sort.Column switch
            {
                PipelineSortColumn.Company => OrderByText(inStatus, application => JobFor(application).Company, sort.Descending),
                PipelineSortColumn.Title => OrderByText(inStatus, application => JobFor(application).Title, sort.Descending),
                PipelineSortColumn.Score => OrderByValue(inStatus, application => JobFor(application).ScoreTotal, sort.Descending),
                PipelineSortColumn.Applied => OrderByValue(inStatus, application => application.AppliedAt, sort.Descending),
                _ => OrderByValue(inStatus, application => application.NextActionDue, sort.Descending)
            };

        return ordered.ThenBy(application => JobFor(application).Company, StringComparer.OrdinalIgnoreCase);
    }

    private static IOrderedEnumerable<Application> OrderByText(IEnumerable<Application> rows, Func<Application, string> key, bool descending)
    {
        return descending ? rows.OrderByDescending(key, StringComparer.OrdinalIgnoreCase) : rows.OrderBy(key, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Orders by a value that can be missing; the rows without one go last in either direction.</summary>
    private static IOrderedEnumerable<Application> OrderByValue<T>(IEnumerable<Application> rows, Func<Application, T?> key, bool descending)
        where T : struct
    {
        IOrderedEnumerable<Application> blanksLast = rows.OrderBy(application => key(application) is null);

        return descending ? blanksLast.ThenByDescending(key) : blanksLast.ThenBy(key);
    }

    /// <summary>Sorts a group by the clicked column, or flips the direction when it is already sorted by it; the score opens highest first, the other columns ascending.</summary>
    private void SortBy(ApplicationStatus status, PipelineSortColumn column)
    {
        sortsByStatus[status] = sortsByStatus.TryGetValue(status, out GroupSort? current) && current.Column == column
            ? current with { Descending = !current.Descending }
            : new GroupSort(column, column == PipelineSortColumn.Score);
    }

    private string? AriaSort(ApplicationStatus status, PipelineSortColumn column)
    {
        if (!sortsByStatus.TryGetValue(status, out GroupSort? sort) || sort.Column != column)
        {
            return null;
        }

        return sort.Descending ? "descending" : "ascending";
    }

    private string SortIcon(ApplicationStatus status, PipelineSortColumn column)
    {
        return AriaSort(status, column) switch
        {
            "ascending" => "bi-caret-up-fill",
            "descending" => "bi-caret-down-fill",
            _ => "bi-chevron-expand inactive"
        };
    }

    /// <summary>The full pay and place text the meta line's ellipsis hides, shown as its tooltip.</summary>
    private string JobMetaTooltip(JobSummary job)
    {
        string? pay = job.HasPay ? JobDisplay.Comp(job.CompMinPerYear, job.CompMaxPerYear, baseCurrency) : null;
        string? place = job.HasPlace ? JobDisplay.Place(job.RemotePolicy, job.LocationText, job.CountryIso) : null;

        return string.Join(" · ", new[] { pay, place }.Where(part => part is not null));
    }

    private string DueRowClass(Application application)
    {
        if (application.NextActionDue is not DateOnly due)
        {
            return string.Empty;
        }

        if (due < today)
        {
            return "row-overdue";
        }

        return due == today ? "row-due" : string.Empty;
    }

    private static string DetailRowKey(Guid applicationId)
    {
        return $"detail-{applicationId}";
    }

    private bool IsExpanded(Guid applicationId)
    {
        return expandedApplicationIds.Contains(applicationId);
    }

    private void ToggleExpanded(Guid applicationId)
    {
        if (!expandedApplicationIds.Add(applicationId))
        {
            expandedApplicationIds.Remove(applicationId);
        }
    }

    private void BeginStatusChange(Application application, ChangeEventArgs args)
    {
        if (args.Value is string text && Enum.TryParse(text, out ApplicationStatus status))
        {
            ApplicationDraft draft = DraftFor(application);
            draft.PendingStatus = status;
            draft.PendingStatusNote = string.Empty;
            draft.PendingChannel = application.Channel ?? ApplicationChannel.Ats;
        }
    }

    private void CancelStatusChange(Application application)
    {
        ApplicationDraft draft = DraftFor(application);
        draft.PendingStatus = null;
        draft.PendingStatusNote = string.Empty;
    }

    private async Task ConfirmStatusChangeAsync(Application application)
    {
        ApplicationDraft draft = DraftFor(application);
        if (draft.PendingStatus is not ApplicationStatus status)
        {
            return;
        }

        string? note = string.IsNullOrWhiteSpace(draft.PendingStatusNote) ? null : draft.PendingStatusNote;
        await ApplicationService.ChangeJobStatusAsync(application.JobId, status, draft.PendingChannel, note);

        await LoadAsync();
    }

    private async Task MarkGhostedAsync(GhostRow row)
    {
        string note = $"Marked ghosted: no status change in at least {ghostThresholdDays} days ({row.Company} - {row.Title}).";
        await ApplicationService.ChangeStatusAsync(row.ApplicationId, ApplicationStatus.Ghosted, note);

        await LoadAsync();
    }

    /// <summary>The handful of job facts a pipeline row shows next to its application.</summary>
    private sealed record JobSummary(
        Guid JobId,
        string Company,
        string Title,
        int? ScoreTotal,
        ScorePoints? Points,
        JobClass? Class,
        decimal? CompMinPerYear,
        decimal? CompMaxPerYear,
        string? RemotePolicy,
        string? LocationText,
        string? CountryIso,
        string ApplicationUrl)
    {
        public static readonly JobSummary Unknown = new(Guid.Empty, "(unknown)", "(unknown)", null, null, null, null, null, null, null, null, string.Empty);

        /// <summary>True when the job states any pay.</summary>
        public bool HasPay => CompMinPerYear is not null || CompMaxPerYear is not null;

        /// <summary>True when the posting or its score says anything about where the role sits.</summary>
        public bool HasPlace => !string.IsNullOrWhiteSpace(RemotePolicy) || !string.IsNullOrWhiteSpace(LocationText) || !string.IsNullOrWhiteSpace(CountryIso);
    }

    /// <summary>One row of the ghost-candidate grid: an application whose status has not moved for at least the threshold, with that status in the words the pages use.</summary>
    private sealed record GhostRow(Guid ApplicationId, string Company, string Title, string Status, string Since);

    /// <summary>The columns a group table can be sorted by.</summary>
    private enum PipelineSortColumn
    {
        Company,
        Title,
        Score,
        Applied,
        Due
    }

    /// <summary>One header of the group table: its label and, when it sorts, the column it sorts by.</summary>
    private sealed record HeaderColumn(string Label, PipelineSortColumn? SortColumn);

    /// <summary>How one group is sorted: the column and the direction.</summary>
    private sealed record GroupSort(PipelineSortColumn Column, bool Descending);

    /// <summary>What a row's status change holds until it is confirmed; the rest of the application edits in its <see cref="Shared.ApplicationPanel"/>.</summary>
    private sealed class ApplicationDraft
    {
        public ApplicationStatus? PendingStatus { get; set; }

        public string PendingStatusNote { get; set; } = string.Empty;

        public ApplicationChannel PendingChannel { get; set; } = ApplicationChannel.Ats;
    }
}
