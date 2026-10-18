using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Pipeline;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Jobs;

/// <summary>Folds the copies of one posting that earlier refreshes stored as separate jobs into one job: the jobs of one company under one title with the same text, such as an opening listed once per country.</summary>
/// <remarks>Runs at every start and finds nothing once the copies are folded, since a refresh now resolves a copy to the job it belongs to. The job that stays is the one with an application, then a triaged one, then an active one, then the one whose place suits the candidate best, then a scored one, then the first seen. It takes the sightings of the others, which are deleted, so a refresh resolves every copy to it instead of bringing one back. A group where more than one job carries an application is left alone, and a job added by hand is never folded.</remarks>
public sealed class CopyFolder(IDbContextFactory<JobHunterDbContext> contextFactory, ILogger<CopyFolder> logger)
{
    /// <summary>Folds every group of copies and returns how many jobs were folded away.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var groups = await context.Jobs
            .Where(job => !job.IsManual)
            .GroupBy(job => new { job.Company, job.Title, job.DescriptionHash })
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToListAsync(cancellationToken);

        if (groups.Count == 0)
        {
            return 0;
        }

        Domain.Settings settings = await context.Settings.SingleAsync(row => row.Id == Domain.Settings.SingletonId, cancellationToken);
        CandidateProfile candidate = CandidateProfile.FromSettings(settings);
        HashSet<Guid> applied = [.. await context.Applications.Select(application => application.JobId).ToListAsync(cancellationToken)];
        DateTimeOffset now = DateTimeOffset.UtcNow;
        int folded = 0;

        foreach (var key in groups)
        {
            List<Job> copies = await context.Jobs
                .Where(job => !job.IsManual && job.Company == key.Company && job.Title == key.Title && job.DescriptionHash == key.DescriptionHash)
                .ToListAsync(cancellationToken);

            if (copies.Count(job => applied.Contains(job.Id)) > 1)
            {
                continue;
            }

            Job kept = copies
                .OrderByDescending(job => applied.Contains(job.Id))
                .ThenByDescending(job => job.Triage != TriageState.New)
                .ThenByDescending(job => job.IsActive)
                .ThenBy(job => GeographyRules.PlaceRank(PrefilterInput.FromJob(job, candidate, settings, now)))
                .ThenByDescending(job => job.Scoring == ScoringState.Scored)
                .ThenBy(job => job.FirstSeenAt)
                .First();

            foreach (Job copy in copies.Where(job => job.Id != kept.Id))
            {
                kept.AbsorbCopy(copy);
                context.Jobs.Remove(copy);
                folded++;
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Copies folded: {Folded} jobs folded into {Groups} groups.", folded, groups.Count);

        return folded;
    }
}
