using JobHunter.Applications;
using JobHunter.Components.Shared;
using JobHunter.Domain;
using JobHunter.Jobs;
using JobHunter.Llm;
using JobHunter.Llm.Exchange;
using JobHunter.Refresh;
using Microsoft.AspNetCore.Components;

namespace JobHunter.Components.Pages;

/// <summary>The inbox: the class A and B jobs that still wait for a pursue or a skip, with the export and import of the path that runs without a key.</summary>
public partial class Inbox
{
    /// <summary>The age the inbox opens narrowed to, in days.</summary>
    private const int DefaultPostedWithinDays = 7;

    private readonly HashSet<Guid> expandedRows = [];
    private readonly Dictionary<Guid, string> descriptions = [];
    private readonly InboxFilter filter = new();

    private InboxPage? inboxPage;
    private Domain.Settings settings = Domain.Settings.CreateDefault();
    private int awaitingScoreCount;
    private Guid busyJobId;
    private bool exchangeBusy;
    private ExchangeExportResult? exportResult;
    private ExchangeImportResult? importResult;
    private int? postedWithinDays;

    [Inject]
    private JobQueryService JobQueries { get; set; } = null!;

    [Inject]
    private TriageService Triage { get; set; } = null!;

    [Inject]
    private JobHunter.Settings.SettingsService SettingsService { get; set; } = null!;

    [Inject]
    private ApiKeyDetector KeyDetector { get; set; } = null!;

    [Inject]
    private ExchangeExporter Exporter { get; set; } = null!;

    [Inject]
    private ExchangeImporter Importer { get; set; } = null!;

    [Inject]
    private ScoreBacklog Backlog { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    [Inject]
    private PageLoading Loading { get; set; } = null!;

    private bool IsBusy => busyJobId != Guid.Empty;

    private bool ExportHintVisible => awaitingScoreCount > 0 && !KeyDetector.IsPresent;

    /// <summary>The rows of the page under one heading per class, A before B; within a class the rows keep the order the inbox query gave them.</summary>
    private IEnumerable<IGrouping<JobClass, InboxRow>> ClassGroups => (inboxPage?.Rows ?? []).GroupBy(row => row.Class).OrderBy(group => group.Key);

    /// <summary>Opens on the jobs posted within the last week.</summary>
    protected override async Task OnInitializedAsync()
    {
        postedWithinDays = DefaultPostedWithinDays;
        filter.PostedSince = PostedSince(postedWithinDays);
        await LoadAsync();
    }

    private async Task PursueAsync(Guid jobId)
    {
        busyJobId = jobId;

        try
        {
            await Triage.PursueAsync(jobId);
        }
        finally
        {
            busyJobId = Guid.Empty;
        }

        Navigation.NavigateTo($"/jobs/{jobId}");
    }

    private async Task SkipAsync(Guid jobId)
    {
        busyJobId = jobId;

        try
        {
            await Triage.SkipAsync(jobId);
            expandedRows.Remove(jobId);
            await LoadPageAsync();
        }
        finally
        {
            busyJobId = Guid.Empty;
        }
    }

    private async Task ExportAsync()
    {
        exchangeBusy = true;

        try
        {
            importResult = null;
            exportResult = await Exporter.ExportAsync();
        }
        finally
        {
            exchangeBusy = false;
        }
    }

    private async Task ImportAsync()
    {
        exchangeBusy = true;

        try
        {
            exportResult = null;
            importResult = await Importer.ImportAsync();
            await Backlog.RecountAsync();
            await LoadAsync();
        }
        finally
        {
            exchangeBusy = false;
        }
    }

    private async Task ToggleAsync(Guid jobId)
    {
        if (!expandedRows.Add(jobId))
        {
            expandedRows.Remove(jobId);

            return;
        }

        if (!descriptions.ContainsKey(jobId))
        {
            descriptions[jobId] = await JobQueries.GetDescriptionAsync(jobId);
        }
    }

    private string Description(Guid jobId)
    {
        return descriptions.TryGetValue(jobId, out string? description) ? description : string.Empty;
    }

    private async Task LoadAsync()
    {
        await LoadPageAsync();
        awaitingScoreCount = await Backlog.CountAsync();
        settings = await SettingsService.GetAsync();
    }

    /// <summary>Reads the current page again; a page that a skip or an import emptied comes back as the last page that still has rows.</summary>
    private async Task LoadPageAsync()
    {
        inboxPage = await Loading.RunAsync(() => JobQueries.GetInboxAsync(filter));
        filter.PageIndex = inboxPage.PageIndex;
    }

    private async Task GoToPageAsync(int pageIndex)
    {
        filter.PageIndex = pageIndex;
        await LoadPageAsync();
    }

    /// <summary>Narrows the inbox to the jobs posted within the days counted back from now, or widens it again when no positive number is given.</summary>
    private async Task FilterByAgeAsync()
    {
        filter.PostedSince = PostedSince(postedWithinDays);
        await GoToPageAsync(0);
    }

    private static DateTimeOffset? PostedSince(int? postedWithinDays)
    {
        return postedWithinDays is int days and > 0 ? DateTimeOffset.UtcNow.AddDays(-days) : null;
    }
}
