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
    private readonly GridSort<JobListRow> ageSort = GridSort<JobListRow>.ByDescending(row => row.FirstSeenAt);

    private List<JobListRow>? rows;
    private IQueryable<JobListRow>? rowQuery;
    private CancellationTokenSource? pendingSearch;

    [Inject]
    private JobQueryService JobQueries { get; set; } = null!;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        pendingSearch?.Cancel();
        pendingSearch?.Dispose();
        pendingSearch = null;
    }

    private async Task LoadAsync()
    {
        rows = [.. await JobQueries.GetJobsAsync(filter)];
        rowQuery = rows.AsQueryable();
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
        filter.Flag = null;
        filter.Triage = null;
        filter.Prefilter = null;
        filter.Text = null;

        await LoadAsync();
    }

    private string CountLine(int shown)
    {
        return shown == filter.Take
            ? $"First {shown} job(s) shown; the list is capped, so narrow the filters to see the rest."
            : $"{shown} job(s) match the filters.";
    }
}
