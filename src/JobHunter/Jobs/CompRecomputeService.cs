using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Llm.Contracts;
using JobHunter.Pipeline;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Jobs;

/// <summary>What a recompute did: how many jobs had their pay converted, how many changed class, or why nothing was changed.</summary>
public sealed record CompRecomputeResult(int JobsWithPay, int ClassesChanged, string? Refusal);

/// <summary>Brings every stored figure into the saved base currency after it changed: each job's pay from its raw amounts, the compensation bounds and the high-pay threshold at the current rate, and the class from the stored score card, without calling any model.</summary>
public sealed class CompRecomputeService(IDbContextFactory<JobHunterDbContext> contextFactory, CompNormalizer compNormalizer, IFxRateProvider fxRates)
{
    /// <summary>Recomputes everything in one save, so either every figure is in the new base currency or none is; refuses without changing anything when no rate links the old currency to the new one.</summary>
    public async Task<CompRecomputeResult> RecomputeAsync(CancellationToken cancellationToken = default)
    {
        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Domain.Settings settings = await context.Settings.SingleAsync(row => row.Id == Domain.Settings.SingletonId, cancellationToken);
        string previousCurrency = settings.CompComputedInCurrency;
        string baseCurrency = settings.BaseCurrency;

        decimal? unitsPerPrevious = await fxRates.GetUnitsPerBaseAsync(baseCurrency, previousCurrency, settings, cancellationToken);
        if (unitsPerPrevious is not decimal rate)
        {
            return new CompRecomputeResult(0, 0, $"No exchange rate from {previousCurrency} to {baseCurrency} is known; fetch the rates or add an FX override for {previousCurrency}, then recompute.");
        }

        settings.RecordCompRecomputedInBaseCurrency(
            ConvertHourly(settings.MinContractorHourly, rate),
            ConvertAnnual(settings.MinEmploymentAnnual, rate),
            ConvertAnnual(settings.TargetAnnual, rate),
            ConvertAnnual(settings.HighPayThresholdPerYear, rate));

        List<Job> jobs = await context.Jobs.ToListAsync(cancellationToken);
        int jobsWithPay = 0;
        int classesChanged = 0;

        foreach (Job job in jobs)
        {
            if (job.CompMin is not null || job.CompMax is not null)
            {
                YearlyComp comp = await compNormalizer.ToBasePerYearAsync(job.CompMin, job.CompMax, job.CompCurrency, job.CompPeriod, baseCurrency, settings, cancellationToken);
                job.RecordCompensation(job.CompMin, job.CompMax, job.CompCurrency, job.CompPeriod, comp.MinPerYear, comp.MaxPerYear, settings.HighPayThresholdPerYear);
                jobsWithPay++;
            }

            if (Reclassify(job, settings))
            {
                classesChanged++;
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        return new CompRecomputeResult(jobsWithPay, classesChanged, null);
    }

    /// <summary>Reclassifies a job whose class comes from its score card, the way scoring does with the same card, and returns whether the class changed.</summary>
    /// <remarks>A job that is not scored, or that the prefilter dropped after scoring, keeps its class: that class does not come from the card.</remarks>
    internal static bool Reclassify(Job job, Domain.Settings settings)
    {
        if (job.Score is not ScoreCard card || job.Scoring != ScoringState.Scored || job.Prefilter != PrefilterState.Passed)
        {
            return false;
        }

        Classification classification = Classifier.Classify(new ClassificationInput(
            new ScoreDimensionsPayload(card.Niche, card.Level, card.Stack, card.RemoteTimezone, card.ContractForm, card.CompSignal, card.CompanySignal),
            card.EmploymentType,
            card.BlockingUnknowns,
            card.RequiresUsAuthorization is true,
            new YearlyComp(job.CompMinPerYear, job.CompMaxPerYear),
            PrefilterDropped: false,
            settings));

        JobClass? previousClass = job.Class;
        job.RecordScore(card with { CompSignal = classification.CompSignal, Total = classification.Total, BlockingUnknowns = [.. card.BlockingUnknowns] }, classification.Class);

        return previousClass != classification.Class;
    }

    private static decimal? ConvertAnnual(decimal? amount, decimal rate)
    {
        return amount is decimal value ? decimal.Round(value * rate, 0, MidpointRounding.AwayFromZero) : null;
    }

    private static decimal? ConvertHourly(decimal? amount, decimal rate)
    {
        return amount is decimal value ? decimal.Round(value * rate, 2, MidpointRounding.AwayFromZero) : null;
    }
}
