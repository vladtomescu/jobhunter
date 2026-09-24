using JobHunter.Data;
using JobHunter.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Jobs;

/// <summary>Recomputes the high-pay flag of every stored job from its pay, touching no other flag and calling no model.</summary>
/// <remarks>Runs at every start and is idempotent: it gives jobs stored before the flag existed their flag, and brings back in line a job whose flag no longer matches its pay.</remarks>
public sealed class HighlightFlagBackfill(IDbContextFactory<JobHunterDbContext> contextFactory, ILogger<HighlightFlagBackfill> logger)
{
    /// <summary>Recomputes the flag on every job, saves the jobs whose flags changed and returns how many did.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        List<Job> jobs = await context.Jobs.ToListAsync(cancellationToken);
        int changed = 0;

        foreach (Job job in jobs)
        {
            JobFlag[] before = [.. job.Flags];

            job.RefreshHighPayFlag();

            if (!before.SequenceEqual(job.Flags))
            {
                changed++;
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Highlight flags recomputed: {Changed} of {Total} jobs changed.", changed, jobs.Count);

        return changed;
    }
}
