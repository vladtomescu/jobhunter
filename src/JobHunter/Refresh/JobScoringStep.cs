using JobHunter.Domain;
using JobHunter.Llm;
using JobHunter.Llm.Contracts;
using JobHunter.Llm.Exchange;
using JobHunter.Pipeline;

namespace JobHunter.Refresh;

/// <summary>What scoring one job came to: scored, or the reason it was not and whether the account ran out of allowance.</summary>
public sealed record JobScoringResult(string? FailureReason, bool UsageLimitReached)
{
    /// <summary>A job that now carries the new score.</summary>
    public static JobScoringResult Success { get; } = new(null, false);

    /// <summary>True when the call produced a score and it was applied to the job.</summary>
    public bool Scored => FailureReason is null;
}

/// <summary>Scores one job and records the result on it, so that a score run and the Score again button of the job page send the same prompt to the same scorer and classify through the same applier; the caller saves the job.</summary>
/// <remarks>The settings type is written qualified because the JobHunter.Settings namespace shadows the plain name.</remarks>
public sealed class JobScoringStep(IJobScorer scorer, ScoreApplier scoreApplier, ILogger<JobScoringStep> logger)
{
    /// <summary>The reason recorded when the scorer answers with neither a payload nor a reason.</summary>
    public const string NoResultReason = "the scorer returned no result";

    /// <summary>Sends the job with the score model of the settings and applies the payload; a failure marks the job Failed with the reason, except on a job that already holds a current score, which a failed attempt leaves standing.</summary>
    public async Task<JobScoringResult> ScoreAsync(Job job, Domain.Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(settings);

        ScoreOutcome outcome = WithinContract(await CallScorerAsync(job, settings, cancellationToken));

        if (outcome.Payload is ScorePayload payload)
        {
            await scoreApplier.ApplyAsync(job, payload, outcome.Model ?? settings.ScoreModel, settings, DateTimeOffset.UtcNow, cancellationToken);

            return JobScoringResult.Success;
        }

        string reason = outcome.FailureReason ?? NoResultReason;

        if (job.Scoring != ScoringState.Scored)
        {
            job.FailScoring(reason);
        }

        return new JobScoringResult(reason, outcome.UsageLimitReached);
    }

    /// <summary>Turns a payload the exchange import would refuse into a failure that is not worth repeating, as a payload that does not match the schema already is.</summary>
    private static ScoreOutcome WithinContract(ScoreOutcome outcome)
    {
        return outcome.Payload is ScorePayload payload && ExchangeImporter.CheckScore(payload) is string problem
            ? ScoreOutcome.Failure($"The model returned a score the contract refuses: {problem}", retryable: false)
            : outcome;
    }

    private async Task<ScoreOutcome> CallScorerAsync(Job job, Domain.Settings settings, CancellationToken cancellationToken)
    {
        try
        {
            return await scorer.ScoreAsync(LlmRequests.ForScore(job, settings.ScoreModel), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Scoring job {JobId} threw.", job.Id);

            return ScoreOutcome.Failure(exception.Message, retryable: true);
        }
    }
}
