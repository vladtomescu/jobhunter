using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Jobs;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Refresh;

/// <summary>Reads the stored refresh runs for the runs page, each as the same summary the refresh panel shows.</summary>
public sealed class RunHistoryService(IDbContextFactory<JobHunterDbContext> contextFactory)
{
    /// <summary>One page of the runs, newest first, including a run still in progress and runs that failed; a page past the last one comes back as the last page.</summary>
    public async Task<RunHistoryPage> GetPageAsync(int pageIndex = 0, CancellationToken cancellationToken = default)
    {
        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        int totalCount = await context.FetchRuns.CountAsync(cancellationToken);
        int clampedPageIndex = Paging.ClampPageIndex(pageIndex, totalCount);

        List<FetchRun> runs = await context.FetchRuns
            .AsNoTracking()
            .OrderByDescending(run => run.StartedAt)
            .Skip(clampedPageIndex * Paging.PageSize)
            .Take(Paging.PageSize)
            .ToListAsync(cancellationToken);

        return new RunHistoryPage([.. runs.Select(RefreshRunSummary.FromRun)], clampedPageIndex, totalCount);
    }
}
