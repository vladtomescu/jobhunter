using JobHunter.Jobs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.QuickGrid;

namespace JobHunter.Components.Pages;

/// <summary>The full job list, dropped jobs included, so that the rules stay auditable.</summary>
public partial class Jobs
{
    private readonly JobListFilter filter = new();
    private readonly GridSort<JobListRow> titleSort = GridSort<JobListRow>.ByAscending(row => row.Title);
    private readonly GridSort<JobListRow> ageSort = GridSort<JobListRow>.ByDescending(row => row.FirstSeenAt);

    private List<JobListRow>? rows;
    private IQueryable<JobListRow>? rowQuery;

    [Inject]
    private JobQueryService JobQueries { get; set; } = default!;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        rows = [.. await JobQueries.GetJobsAsync(filter)];
        rowQuery = rows.AsQueryable();
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
}
