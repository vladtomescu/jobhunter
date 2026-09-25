using JobHunter.Domain;
using JobHunter.Llm.Contracts;

namespace JobHunter.Pipeline;

/// <summary>Puts a score on a job: it recomputes the compensation signal, normalizes the compensation, classifies and stores the score card.</summary>
/// <remarks>The scoring step of a refresh and the import of externally scored jobs both go through here, so both paths classify identically.</remarks>
public sealed class ScoreApplier(CompNormalizer compNormalizer)
{
    /// <summary>Applies one scored payload to its job and returns what classification decided.</summary>
    /// <remarks>The settings type is written qualified because the JobHunter.Settings namespace shadows the plain name.</remarks>
    public async Task<Classification> ApplyAsync(Job job, ScorePayload payload, string model, Domain.Settings settings, DateTimeOffset scoredAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(settings);

        bool statedBySource = job.CompMin is not null || job.CompMax is not null;
        decimal? compMin = statedBySource ? job.CompMin : payload.Facts.Comp.Min;
        decimal? compMax = statedBySource ? job.CompMax : payload.Facts.Comp.Max;
        string? currency = statedBySource ? job.CompCurrency : payload.Facts.Comp.Currency;
        CompPeriod? period = statedBySource ? job.CompPeriod : CompNormalizer.ParsePeriod(payload.Facts.Comp.Period);

        YearlyComp comp = await compNormalizer.ToBasePerYearAsync(compMin, compMax, currency, period, settings.BaseCurrency, settings, cancellationToken);
        job.RecordCompensation(compMin, compMax, currency, period, comp.MinPerYear, comp.MaxPerYear, settings.HighPayThresholdPerYear);

        if (compMin is not null || compMax is not null)
        {
            job.ClearCompensationUnknownFlag();
        }

        bool requiresUsAuthorization = payload.Facts.RequiresUsAuthorization is true;
        job.RecordWorkAuthorizationRequirement(requiresUsAuthorization);

        Classification classification = Classifier.Classify(new ClassificationInput(
            payload.Scores,
            payload.Facts.EmploymentType,
            payload.BlockingUnknowns,
            requiresUsAuthorization,
            payload.Facts.EndClientNamed,
            comp,
            job.Prefilter == PrefilterState.Dropped,
            settings));

        job.RecordScore(BuildScoreCard(job, payload, classification, compMin, compMax, currency, period, model, scoredAt), classification.Class);

        return classification;
    }

    private static ScoreCard BuildScoreCard(Job job, ScorePayload payload, Classification classification, decimal? compMin, decimal? compMax, string? currency, CompPeriod? period, string model, DateTimeOffset scoredAt)
    {
        return new ScoreCard(
            payload.Scores.Niche,
            payload.Scores.Level,
            payload.Scores.Stack,
            payload.Scores.RemoteTimezone,
            payload.Scores.ContractForm,
            classification.CompSignal,
            payload.Scores.CompanySignal,
            classification.Total,
            payload.Reasoning,
            payload.Facts.LevelGuess,
            payload.Facts.RemotePolicy,
            payload.Facts.EmploymentType,
            compMin,
            compMax,
            currency,
            period,
            payload.Facts.TimezoneNote,
            payload.Facts.RequiresUsAuthorization,
            payload.Facts.EndClientNamed,
            payload.Facts.AiMeaning,
            [.. payload.BlockingUnknowns],
            model,
            scoredAt,
            job.DescriptionHash);
    }
}
