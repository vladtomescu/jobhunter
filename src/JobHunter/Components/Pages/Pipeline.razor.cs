using System.Globalization;
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

    private static readonly ApplicationStatus[] StatusOrder = Enum.GetValues<ApplicationStatus>();

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
            draft = ApplicationDraft.From(application);
            drafts[application.Id] = draft;
        }

        return draft;
    }

    private IEnumerable<Application> ApplicationsInStatus(ApplicationStatus status)
    {
        return applications
            .Where(application => application.Status == status)
            .OrderBy(application => application.NextActionDue ?? DateOnly.MaxValue)
            .ThenBy(application => JobFor(application).Company, StringComparer.OrdinalIgnoreCase);
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

    private static string ReadInputText(ChangeEventArgs args)
    {
        return args.Value as string ?? string.Empty;
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
        await ApplicationService.ChangeStatusAsync(application.Id, status, note);

        await LoadAsync();
    }

    private async Task MarkGhostedAsync(GhostRow row)
    {
        string note = $"Marked ghosted: no status change in at least {ghostThresholdDays} days ({row.Company} - {row.Title}).";
        await ApplicationService.ChangeStatusAsync(row.ApplicationId, ApplicationStatus.Ghosted, note);

        await LoadAsync();
    }

    private async Task SaveNextActionAsync(Application application)
    {
        ApplicationDraft draft = DraftFor(application);

        await ApplicationService.SetNextActionAsync(application.Id, draft.NextAction, draft.ReadNextActionDue());
        await LoadAsync();
    }

    private async Task SaveContactAsync(Application application)
    {
        ApplicationDraft draft = DraftFor(application);

        await ApplicationService.SetContactAsync(application.Id, draft.ContactName, draft.ContactRole, draft.ContactLink);
        await LoadAsync();
    }

    private async Task SaveCompDiscussedAsync(Application application)
    {
        ApplicationDraft draft = DraftFor(application);

        await ApplicationService.SetCompDiscussedAsync(application.Id, draft.CompDiscussed);
        await LoadAsync();
    }

    private async Task AddNoteAsync(Application application)
    {
        ApplicationDraft draft = DraftFor(application);
        if (string.IsNullOrWhiteSpace(draft.NewNote))
        {
            return;
        }

        await ApplicationService.AddNoteAsync(application.Id, draft.NewNote);
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

    /// <summary>What the inline editors of one pipeline row hold until that row is saved; the due date travels as the text the date input shows.</summary>
    private sealed class ApplicationDraft
    {
        private const string DueDateFormat = "yyyy-MM-dd";

        /// <summary>Starts a draft whose fields mirror the stored application.</summary>
        public static ApplicationDraft From(Application application)
        {
            return new ApplicationDraft
            {
                NextAction = application.NextAction ?? string.Empty,
                NextActionDueText = application.NextActionDue?.ToString(DueDateFormat, CultureInfo.InvariantCulture) ?? string.Empty,
                ContactName = application.Contact?.Name ?? string.Empty,
                ContactRole = application.Contact?.Role ?? string.Empty,
                ContactLink = application.Contact?.Link ?? string.Empty,
                CompDiscussed = application.CompDiscussed ?? string.Empty
            };
        }

        public string NextAction { get; set; } = string.Empty;

        public string NextActionDueText { get; set; } = string.Empty;

        /// <summary>The due date the row carries, or null when the field is empty, which clears the date.</summary>
        public DateOnly? ReadNextActionDue()
        {
            return DateOnly.TryParseExact(NextActionDueText.Trim(), DueDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly due) ? due : null;
        }

        public string ContactName { get; set; } = string.Empty;

        public string ContactRole { get; set; } = string.Empty;

        public string ContactLink { get; set; } = string.Empty;

        public string CompDiscussed { get; set; } = string.Empty;

        public string NewNote { get; set; } = string.Empty;

        public ApplicationStatus? PendingStatus { get; set; }

        public string PendingStatusNote { get; set; } = string.Empty;
    }
}
