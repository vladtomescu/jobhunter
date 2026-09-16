using JobHunter.Applications;
using JobHunter.Data;
using JobHunter.Domain;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Components.Pages;

/// <summary>Code-behind for the pipeline page: applications grouped by status with inline status, next-action and detail editing, plus the ghost-candidate grid.</summary>
/// <remarks>The grouped grid is hand-written table markup because QuickGrid renders exactly one row per item and cannot host the expandable detail row that the contact, notes and comp panel needs; the flat ghost-candidate list is a QuickGrid.</remarks>
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

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToLocalTime().DateTime);
        Domain.Settings settings = await SettingsService.GetAsync();
        ghostThresholdDays = settings.GhostThresholdDays;

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
            .Select(job => new JobSummary(job.Id, job.Company, job.Title))
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
            application.Status,
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

    private string DueRowClass(Application application)
    {
        if (application.NextActionDue is not DateOnly due)
        {
            return string.Empty;
        }

        if (due <= today)
        {
            return "row-overdue";
        }

        return due <= today.AddDays(3) ? "row-due" : string.Empty;
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

        await ApplicationService.SetNextActionAsync(application.Id, draft.NextAction, draft.NextActionDue);
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
    private sealed record JobSummary(Guid JobId, string Company, string Title)
    {
        public static readonly JobSummary Unknown = new(Guid.Empty, "(unknown)", "(unknown)");
    }

    /// <summary>One row of the ghost-candidate grid: an application whose status has not moved for at least the threshold.</summary>
    private sealed record GhostRow(Guid ApplicationId, string Company, string Title, ApplicationStatus Status, string Since);

    /// <summary>What the inline editors of one pipeline row hold until that row is saved; the date input binds through its edit context.</summary>
    private sealed class ApplicationDraft
    {
        private ApplicationDraft()
        {
            EditContext = new EditContext(this);
        }

        /// <summary>Starts a draft whose fields mirror the stored application.</summary>
        public static ApplicationDraft From(Application application)
        {
            return new ApplicationDraft
            {
                NextAction = application.NextAction ?? string.Empty,
                NextActionDue = application.NextActionDue,
                ContactName = application.Contact?.Name ?? string.Empty,
                ContactRole = application.Contact?.Role ?? string.Empty,
                ContactLink = application.Contact?.Link ?? string.Empty,
                CompDiscussed = application.CompDiscussed ?? string.Empty
            };
        }

        public EditContext EditContext { get; }

        public string NextAction { get; set; } = string.Empty;

        public DateOnly? NextActionDue { get; set; }

        public string ContactName { get; set; } = string.Empty;

        public string ContactRole { get; set; } = string.Empty;

        public string ContactLink { get; set; } = string.Empty;

        public string CompDiscussed { get; set; } = string.Empty;

        public string NewNote { get; set; } = string.Empty;

        public ApplicationStatus? PendingStatus { get; set; }

        public string PendingStatusNote { get; set; } = string.Empty;
    }
}
