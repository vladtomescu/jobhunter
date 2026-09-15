using JobHunter.Domain;
using JobHunter.Llm.Contracts;

namespace JobHunter.Applications;

/// <summary>Maps a job's stored score card back into the score payload shape the kit writer expects, since the payload itself is never persisted.</summary>
public static class ScoreCardMapper
{
    /// <summary>Builds the score payload for one job from its stored score card.</summary>
    public static ScorePayload ToScorePayload(Guid jobId, ScoreCard score)
    {
        ArgumentNullException.ThrowIfNull(score);

        return new ScorePayload(
            jobId.ToString(),
            new ScoreDimensionsPayload(score.Niche, score.Level, score.Stack, score.RemoteTimezone, score.ContractForm, score.CompSignal, score.CompanySignal),
            new ScoreFactsPayload(
                score.LevelGuess,
                score.RemotePolicy,
                score.EmploymentType,
                new ScoreCompPayload(score.CompMin, score.CompMax, score.CompCurrency, score.CompPeriod?.ToString().ToLowerInvariant()),
                score.TimezoneNote,
                score.RequiresUsAuthorization,
                score.EndClientNamed,
                score.AiMeaning),
            [.. score.BlockingUnknowns],
            score.Reasoning);
    }
}
