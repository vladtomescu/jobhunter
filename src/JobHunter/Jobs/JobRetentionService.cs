using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Settings;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Jobs;

/// <summary>What a retention call found or removed: the number of jobs, or the reason the call was refused.</summary>
public sealed record JobRetentionOutcome(int Jobs, string? Refusal)
{
    /// <summary>A call that went through, with the number of jobs it counted or deleted.</summary>
    public static JobRetentionOutcome Done(int jobs)
    {
        return new JobRetentionOutcome(jobs, null);
    }

    /// <summary>A call refused before it touched anything.</summary>
    public static JobRetentionOutcome Refused(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new JobRetentionOutcome(0, reason);
    }

    /// <summary>True when the call was refused.</summary>
    public bool IsRefused => Refusal is not null;
}

/// <summary>Deletes the jobs older than a number of days, counting a job's age from its posted date or, without one, from the day it was first seen, the same age the job lists show.</summary>
/// <remarks>A job with an application and a job added by hand are never deleted. The number of days cannot go below the first-run window: a refresh takes in postings that young, so a deleted job inside the window would come back from the boards as a new job.</remarks>
public sealed class JobRetentionService(IDbContextFactory<JobHunterDbContext> contextFactory, SettingsService settingsService)
{
    /// <summary>Counts the jobs a delete with the same number of days would remove, or refuses a number below the first-run window.</summary>
    public async Task<JobRetentionOutcome> CountJobsOlderThanAsync(int days, CancellationToken cancellationToken = default)
    {
        if (await RefusalForAsync(days, cancellationToken) is string refusal)
        {
            return JobRetentionOutcome.Refused(refusal);
        }

        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return JobRetentionOutcome.Done(await OlderThan(context, days).CountAsync(cancellationToken));
    }

    /// <summary>Deletes the jobs older than the number of days, keeping every job with an application and every job added by hand, or refuses a number below the first-run window.</summary>
    public async Task<JobRetentionOutcome> DeleteJobsOlderThanAsync(int days, CancellationToken cancellationToken = default)
    {
        if (await RefusalForAsync(days, cancellationToken) is string refusal)
        {
            return JobRetentionOutcome.Refused(refusal);
        }

        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return JobRetentionOutcome.Done(await OlderThan(context, days).ExecuteDeleteAsync(cancellationToken));
    }

    /// <summary>The jobs past the age limit that nothing else holds on to: no application points at them and they were not added by hand.</summary>
    private static IQueryable<Job> OlderThan(JobHunterDbContext context, int days)
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow.AddDays(-days);

        return context.Jobs.Where(job => !job.IsManual
            && (job.PostedAt ?? job.FirstSeenAt) < cutoff
            && !context.Applications.Any(application => application.JobId == job.Id));
    }

    private async Task<string?> RefusalForAsync(int days, CancellationToken cancellationToken)
    {
        Domain.Settings settings = await settingsService.GetAsync(cancellationToken);
        int minimum = settings.FirstRunWindowDays;

        return days < minimum
            ? $"Enter at least {minimum} days, the first-run window: a refresh takes in postings up to {minimum} days old, so a younger job deleted now could come back from the boards as a new job."
            : null;
    }
}
