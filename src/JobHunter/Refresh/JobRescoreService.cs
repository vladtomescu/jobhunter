using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Llm;
using JobHunter.Settings;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Refresh;

/// <summary>What one Score again press came to: the new total and class, or the reason the job was not scored.</summary>
public sealed record RescoreResult(int? Total, JobClass? Class, string? FailureReason)
{
    /// <summary>A job that now carries the new score.</summary>
    public static RescoreResult Scored(int total, JobClass jobClass)
    {
        return new RescoreResult(total, jobClass, null);
    }

    /// <summary>A press that produced no score, with the reason shown on the page.</summary>
    public static RescoreResult NotScored(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new RescoreResult(null, null, reason);
    }

    /// <summary>The sentence the job page shows for this result.</summary>
    public string Describe()
    {
        return FailureReason is string reason
            ? $"Scoring failed: {reason.TrimEnd('.')}."
            : $"Scored again: {Total} of 14, class {Class}.";
    }
}

/// <summary>Scores one job at once, from its page, through the same scoring step a score run uses; it is not counted against the per-run cap, and it never touches the triage or the application of the job.</summary>
/// <remarks>The settings type is written qualified because the JobHunter.Settings namespace shadows the plain name.</remarks>
public sealed class JobRescoreService(
    IDbContextFactory<JobHunterDbContext> contextFactory,
    SettingsService settingsService,
    JobScoringStep scoringStep,
    ApiKeyDetector apiKeyDetector)
{
    /// <summary>What the page shows when the job is no longer stored.</summary>
    public const string MissingJobMessage = "no job is stored under that identifier";

    /// <summary>Scores the job with the current score model and saves the result; without a key nothing is sent and the job keeps its state, exactly as a score run leaves it.</summary>
    public async Task<RescoreResult> RescoreAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        if (!apiKeyDetector.IsPresent)
        {
            return RescoreResult.NotScored(ApiKeyDetector.MissingKeyMessage);
        }

        Domain.Settings settings = await settingsService.GetAsync(cancellationToken);
        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        Job? job = await context.Jobs.FirstOrDefaultAsync(candidate => candidate.Id == jobId, cancellationToken);

        if (job is null)
        {
            return RescoreResult.NotScored(MissingJobMessage);
        }

        JobScoringResult result = await scoringStep.ScoreAsync(job, settings, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return result.FailureReason is string reason
            ? RescoreResult.NotScored(reason)
            : RescoreResult.Scored(job.Score!.Total, job.Class!.Value);
    }
}
