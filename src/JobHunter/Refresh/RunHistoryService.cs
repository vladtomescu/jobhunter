using JobHunter.Data;
using JobHunter.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Refresh;

/// <summary>Reads the stored refresh runs for the runs page, each as the same summary the refresh panel shows.</summary>
public sealed class RunHistoryService(IDbContextFactory<JobHunterDbContext> contextFactory)
{
    /// <summary>How many runs one page of the history carries.</summary>
    public const int DefaultTake = 100;

    /// <summary>The most recent runs, newest first, including a run still in progress and runs that failed.</summary>
    public async Task<IReadOnlyList<RefreshRunSummary>> GetRunsAsync(int take = DefaultTake, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);

        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        List<FetchRun> runs = await context.FetchRuns
            .AsNoTracking()
            .OrderByDescending(run => run.StartedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        return [.. runs.Select(RefreshRunSummary.FromRun)];
    }
}
