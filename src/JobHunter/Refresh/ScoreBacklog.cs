using JobHunter.Data;
using JobHunter.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Refresh;

/// <summary>The jobs waiting for a score: active, passed the prefilter and carrying no score; it counts them and keeps the count on the shared state for the Score button.</summary>
public sealed class ScoreBacklog(IDbContextFactory<JobHunterDbContext> contextFactory, RefreshState state, ILogger<ScoreBacklog> logger)
{
    /// <summary>The jobs a score run picks from.</summary>
    public static IQueryable<Job> Of(JobHunterDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Jobs.Where(job => job.IsActive && job.Prefilter == PrefilterState.Passed && job.Scoring != ScoringState.Scored);
    }

    /// <summary>How many jobs wait for a score.</summary>
    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await Of(context).CountAsync(cancellationToken);
    }

    /// <summary>Counts the jobs waiting for a score again and puts the count on the shared state; a count that fails is logged and leaves the previous one in place, because it only feeds the button.</summary>
    public async Task RecountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            state.RecordAwaitingScore(await CountAsync(cancellationToken));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "The jobs waiting for a score could not be counted.");
        }
    }
}
