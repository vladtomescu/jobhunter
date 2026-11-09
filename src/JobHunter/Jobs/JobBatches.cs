using JobHunter.Data;
using JobHunter.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Jobs;

/// <summary>Walks a selection of stored jobs in batches, so a pass over every job never holds them all in memory.</summary>
/// <remarks>Loading all jobs at once with their posting text took about 700 MB at startup, and the runtime kept that memory afterwards.</remarks>
public static class JobBatches
{
    /// <summary>How many jobs one batch loads.</summary>
    public const int Size = 500;

    /// <summary>Runs the action on every selected job, one batch at a time; saves each batch and forgets its jobs before loading the next. Returns how many jobs were selected.</summary>
    /// <remarks>The selection is read once as a list of ids, so the action may change the fields the selection filters on.</remarks>
    public static async Task<int> ForEachAsync(JobHunterDbContext context, IQueryable<Job> selection, Action<Job> action, CancellationToken cancellationToken)
    {
        List<Guid> jobIds = await selection.Select(job => job.Id).ToListAsync(cancellationToken);

        foreach (Guid[] batch in jobIds.Chunk(Size))
        {
            List<Job> jobs = await context.Jobs.Where(job => batch.Contains(job.Id)).ToListAsync(cancellationToken);

            foreach (Job job in jobs)
            {
                action(job);
            }

            await context.SaveChangesAsync(cancellationToken);
            context.ChangeTracker.Clear();
        }

        return jobIds.Count;
    }
}
