using JobHunter.Domain;
using JobHunter.Jobs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.QuickGrid;

namespace JobHunter.Components.Pages;

/// <summary>The full job list, dropped jobs included, so that the rules stay auditable.</summary>
public partial class Jobs : IDisposable
{
    /// <summary>How long the typing pauses before the search text reaches the query.</summary>
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(300);

    private readonly JobListFilter filter = new();
    private readonly GridSort<JobListRow> titleSort = GridSort<JobListRow>.ByAscending(row => row.Title);

    /// <summary>An ascending age is the newest first, so the sort runs descending; the grid opens on it. The grid takes only a plain property here, so it names the column and the query orders by the posting date, or the first sighting without one.</summary>
    private readonly GridSort<JobListRow> ageSort = GridSort<JobListRow>.ByDescending(row => row.FirstSeenAt);

    private GridItemsProvider<JobListRow> rowsProvider = null!;
    private QuickGrid<JobListRow>? grid;
    private int? totalCount;
    private int? postedWithinDays;
    private Domain.Settings settings = Domain.Settings.CreateDefault();
    private CancellationTokenSource? pendingSearch;

    [Inject]
    private JobQueryService JobQueries { get; set; } = null!;

    [Inject]
    private JobHunter.Settings.SettingsService SettingsService { get; set; } = null!;

    /// <summary>The scoring state the list opens narrowed to, so that a run on the runs page can link straight to its failed jobs.</summary>
    [SupplyParameterFromQuery(Name = "scoring")]
    private string? InitialScoring { get; set; }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        if (Enum.TryParse(InitialScoring, ignoreCase: true, out ScoringState scoring) && Enum.IsDefined(scoring))
        {
            filter.Scoring = scoring;
        }

        settings = await SettingsService.GetAsync();

        // The grid re-reads its rows whenever the delegate it was given changes, so the delegate is made once.
        rowsProvider = ProvideRowsAsync;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        pendingSearch?.Cancel();
        pendingSearch?.Dispose();
        pendingSearch = null;
    }

    /// <summary>Reads the first page again, which is where every change of the filters lands.</summary>
    private async Task LoadAsync()
    {
        await GoToPageAsync(0);
    }

    private async Task GoToPageAsync(int pageIndex)
    {
        filter.PageIndex = pageIndex;
        await grid!.RefreshDataAsync();
    }

    private async ValueTask<GridItemsProviderResult<JobListRow>> ProvideRowsAsync(GridItemsProviderRequest<JobListRow> request)
    {
        ApplyOrder(request);

        JobListPage page = await JobQueries.GetJobsAsync(filter, request.CancellationToken);
        filter.PageIndex = page.PageIndex;
        totalCount = page.TotalCount;
        await InvokeAsync(StateHasChanged);

        return GridItemsProviderResult.From([.. page.Rows], page.TotalCount);
    }

    /// <summary>Carries the column the grid is sorted by into the filter, so that the order holds across pages; the grid opens on the newest first.</summary>
    private void ApplyOrder(GridItemsProviderRequest<JobListRow> request)
    {
        foreach (SortedProperty sorted in request.GetSortByProperties())
        {
            JobListColumn column = sorted.PropertyName switch
            {
                nameof(JobListRow.Company) => JobListColumn.Company,
                nameof(JobListRow.Title) => JobListColumn.Title,
                _ => JobListColumn.Age,
            };
            filter.SortBy(column, sorted.Direction == SortDirection.Descending);

            return;
        }
    }

    private async Task SearchAsync()
    {
        CancellationTokenSource typing = new();
        CancellationTokenSource? previous = pendingSearch;
        pendingSearch = typing;
        previous?.Cancel();
        previous?.Dispose();

        try
        {
            await Task.Delay(SearchDelay, typing.Token);
            await LoadAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ClearAsync()
    {
        filter.Class = null;
        filter.Source = null;
        filter.Flags = [];
        filter.Triage = null;
        filter.Prefilter = null;
        filter.Scoring = null;
        filter.Text = null;
        filter.PostedSince = null;
        postedWithinDays = null;

        await LoadAsync();
    }

    /// <summary>Narrows the list to the jobs posted within the days counted back from now, or widens it again when no positive number is given.</summary>
    private async Task FilterByAgeAsync()
    {
        filter.PostedSince = postedWithinDays is int days and > 0 ? DateTimeOffset.UtcNow.AddDays(-days) : null;
        await LoadAsync();
    }
}
