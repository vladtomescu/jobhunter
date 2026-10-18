using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Llm.Contracts;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Pipeline;

/// <summary>Puts a score on a job: it records the skills the posting asks for in the skill catalog, recomputes the compensation signal and the stack, normalizes the compensation, classifies and stores the score card.</summary>
/// <remarks>The scoring step of a score run and the import of externally scored jobs both go through here, so both paths classify identically. The skills are recorded under the gate every write to the skill catalog shares, since a score run applies several scores side by side, two of them may name the same new skill, and the candidate may mark or merge a skill meanwhile.</remarks>
public sealed class ScoreApplier(CompNormalizer compNormalizer, IDbContextFactory<JobHunterDbContext> contextFactory, SkillCatalogGate skillGate)
{
    /// <summary>Applies one scored payload to its job and returns what classification decided.</summary>
    /// <remarks>The settings type is written qualified because the JobHunter.Settings namespace shadows the plain name. The skills are saved to the catalog at once, on a context of their own; the job is saved by the caller.</remarks>
    public async Task<Classification> ApplyAsync(Job job, ScorePayload payload, string model, Domain.Settings settings, DateTimeOffset scoredAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(settings);

        SkillCatalog catalog = await RecordSkillsAsync(payload.Skills, cancellationToken);
        List<ScoreSkill> skills = [.. payload.Skills.Select(skill => new ScoreSkill(skill.Name, skill.Importance, skill.Category, skill.CvMatch))];

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
            GeographyRules.IsOutsideHomeRegion(job.LocationText, job.RegionText, job.CountryIso, settings.HomeCountryIso, settings.HomeCity, CandidateProfile.ReadHomeRegion(settings)),
            comp,
            job.Prefilter == PrefilterState.Dropped,
            settings,
            SkillCoverage.Of(skills, catalog)));

        job.RecordScore(BuildScoreCard(job, payload, skills, classification, compMin, compMax, currency, period, model, scoredAt), classification.Class);

        return classification;
    }

    /// <summary>Resolves each skill through the catalog: a name no skill carries becomes a new skill with the model's category and CV judgement, and a known skill without a CV mark takes the model's; a mark already set is kept.</summary>
    /// <returns>The catalog as it stands once the skills are recorded, for the skill coverage to read the marks from; empty when the posting names no skill.</returns>
    private async Task<SkillCatalog> RecordSkillsAsync(IReadOnlyList<ScoreSkillPayload> skills, CancellationToken cancellationToken)
    {
        if (skills.Count == 0)
        {
            return new SkillCatalog([]);
        }

        return await skillGate.RunAsync(
            async () =>
            {
                await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
                SkillCatalog catalog = new(await context.Skills.ToListAsync(cancellationToken));

                foreach (ScoreSkillPayload extracted in skills)
                {
                    CvMark cvMark = Enum.Parse<CvMark>(extracted.CvMatch, ignoreCase: true);

                    if (catalog.Resolve(extracted.Name) is Skill known)
                    {
                        known.FillCvMarkFromModel(cvMark);

                        continue;
                    }

                    Skill created = Skill.CreateFromModel(extracted.Name, Enum.Parse<SkillCategory>(extracted.Category, ignoreCase: true), cvMark);
                    catalog.Add(created);
                    context.Skills.Add(created);
                }

                await context.SaveChangesAsync(cancellationToken);

                return catalog;
            },
            cancellationToken);
    }

    private static ScoreCard BuildScoreCard(Job job, ScorePayload payload, List<ScoreSkill> skills, Classification classification, decimal? compMin, decimal? compMax, string? currency, CompPeriod? period, string model, DateTimeOffset scoredAt)
    {
        return new ScoreCard(
            payload.Scores.Niche,
            payload.Scores.Level,
            classification.Stack,
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
            job.DescriptionHash)
        {
            Skills = skills,
            ModelStack = payload.Scores.Stack
        };
    }
}
