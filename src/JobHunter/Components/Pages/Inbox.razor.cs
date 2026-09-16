using JobHunter.Applications;
using JobHunter.Jobs;
using JobHunter.Llm;
using JobHunter.Llm.Exchange;
using Microsoft.AspNetCore.Components;

namespace JobHunter.Components.Pages;

/// <summary>The inbox: the class A and B jobs that still wait for a pursue or a skip, with the export and import of the path that runs without a key.</summary>
public partial class Inbox
{
    private readonly HashSet<Guid> expandedRows = [];
    private readonly Dictionary<Guid, string> descriptions = [];

    private List<InboxRow>? rows;
    private int awaitingScoreCount;
    private Guid busyJobId;
    private bool exchangeBusy;
    private ExchangeExportResult? exportResult;
    private ExchangeImportResult? importResult;

    [Inject]
    private JobQueryService JobQueries { get; set; } = null!;

    [Inject]
    private TriageService Triage { get; set; } = null!;

    [Inject]
    private ApiKeyDetector KeyDetector { get; set; } = null!;

    [Inject]
    private ExchangeExporter Exporter { get; set; } = null!;

    [Inject]
    private ExchangeImporter Importer { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    private bool IsBusy => busyJobId != Guid.Empty;

    private bool ExportHintVisible => awaitingScoreCount > 0 && !KeyDetector.IsPresent;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
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
            rows?.RemoveAll(row => row.Id == jobId);
            expandedRows.Remove(jobId);
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
        rows = [.. await JobQueries.GetInboxAsync()];
        awaitingScoreCount = await JobQueries.CountJobsAwaitingScoreAsync();
    }
}
