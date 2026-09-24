using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Pipeline;
using JobHunter.Settings;
using JobHunter.Sources;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Jobs;

/// <summary>A job entered by hand, for the postings no source covers.</summary>
public sealed record ManualJobRequest(string ApplyUrl, string Title, string Company, string? DescriptionText, string? LocationText, string? CompText);

/// <summary>The job a manual entry led to, and whether it was already known from an earlier sighting.</summary>
public sealed record ManualJobResult(Guid JobId, bool AlreadyKnown);

/// <summary>Creates the jobs entered by hand, through the same normalization, compensation and prefilter steps a fetched job goes through.</summary>
public sealed class ManualJobService(IDbContextFactory<JobHunterDbContext> contextFactory, SettingsService settingsService, Prefilter prefilter, CompNormalizer compNormalizer)
{
    /// <summary>Stores the job and leaves it unscored, so that the next refresh scores it; a link that is already known is returned instead of stored twice.</summary>
    public async Task<ManualJobResult> CreateAsync(ManualJobRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string postingUrl = request.ApplyUrl.Trim();
        string canonicalUrl = UrlCanonicalizer.Canonicalize(postingUrl);
        string fingerprint = JobFingerprint.ForCanonicalUrl(canonicalUrl);

        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Job? known = await context.Jobs.FirstOrDefaultAsync(candidate => candidate.Fingerprint == fingerprint, cancellationToken);
        if (known is not null)
        {
            known.RecordSource(JobSourceKind.Manual, canonicalUrl, now);
            await context.SaveChangesAsync(cancellationToken);

            return new ManualJobResult(known.Id, true);
        }

        Domain.Settings settings = await settingsService.GetAsync(cancellationToken);
        string description = ToPlainText(request.DescriptionText);

        Job job = Job.Create(
            fingerprint,
            canonicalUrl,
            postingUrl,
            request.Company.Trim(),
            request.Title.Trim(),
            description,
            JobFingerprint.ForDescription(description),
            now,
            isManual: true);

        job.RecordSource(JobSourceKind.Manual, canonicalUrl, now);
        job.RecordPostingFacts(postingUrl, null, AtsKindParser.Parse(null, postingUrl), [], null, null);
        job.RecordPlace(Cleaned(request.LocationText), null, null, null, null);

        ManualComp comp = ManualCompParser.Parse(request.CompText);
        if (!comp.IsUnknown)
        {
            YearlyComp normalized = await compNormalizer.ToBasePerYearAsync(comp.Min, comp.Max, comp.Currency, comp.Period, settings.BaseCurrency, settings, cancellationToken);
            job.RecordCompensation(comp.Min, comp.Max, comp.Currency, comp.Period, normalized.MinPerYear, normalized.MaxPerYear, settings.HighPayThresholdPerYear);
        }

        PrefilterVerdict verdict = prefilter.Evaluate(PrefilterInput.FromJob(job, settings, now));
        job.ApplyPrefilterVerdict(verdict.State, verdict.DropReason, verdict.Flags, settings.HighPayThresholdPerYear);

        context.Jobs.Add(job);
        await context.SaveChangesAsync(cancellationToken);

        return new ManualJobResult(job.Id, false);
    }

    private static string ToPlainText(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return string.Empty;
        }

        return HtmlToText.LooksLikeHtml(description) ? HtmlToText.Convert(description) : description.Trim();
    }

    private static string? Cleaned(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
