using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Pipeline;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Jobs;

/// <summary>Deletes the stored jobs of the companies the settings exclude, so they leave the lists and the stats; a refresh never stores a new posting of theirs.</summary>
/// <remarks>Runs at every start and after every settings save. A job with an application and a job added by hand are never deleted. A company taken off the list comes back with its postings the next refresh takes in.</remarks>
public sealed class ExcludedCompanySweep(IDbContextFactory<JobHunterDbContext> contextFactory, ILogger<ExcludedCompanySweep> logger)
{
    /// <summary>Deletes the jobs of the excluded companies and returns how many were deleted.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        Domain.Settings settings = await context.Settings.SingleAsync(row => row.Id == Domain.Settings.SingletonId, cancellationToken);
        CandidateProfile candidate = CandidateProfile.FromSettings(settings);

        var stored = await context.Jobs
            .Where(job => !job.IsManual && !context.Applications.Any(application => application.JobId == job.Id))
            .Select(job => new { job.Id, job.Company })
            .ToListAsync(cancellationToken);

        List<Guid> excluded = [.. stored.Where(job => candidate.ExcludesCompany(job.Company)).Select(job => job.Id)];
        if (excluded.Count == 0)
        {
            return 0;
        }

        int deleted = await context.Jobs.Where(job => excluded.Contains(job.Id)).ExecuteDeleteAsync(cancellationToken);
        logger.LogInformation("Excluded companies applied: {Deleted} jobs deleted.", deleted);

        return deleted;
    }
}
