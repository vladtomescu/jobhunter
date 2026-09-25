using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Pipeline;
using JobHunter.Settings;
using JobHunter.Sources;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Jobs;

/// <summary>A job entered by hand, for the postings no source covers.</summary>
public sealed record ManualJobRequest(string ApplyUrl, string Title, string Company, string? DescriptionText, string? LocationText, string? CompText)
{
    /// <summary>The entry a stored manual job reads back as, so that the edit form opens on what the job holds; the pay is written the way the add form reads it.</summary>
    public static ManualJobRequest FromJob(Job job)
    {
        ArgumentNullException.ThrowIfNull(job);

        return new ManualJobRequest(
            job.PostingUrl,
            job.Title,
            job.Company,
            job.DescriptionText,
            job.LocationText,
            ManualCompParser.Describe(job.CompMin, job.CompMax, job.CompCurrency, job.CompPeriod));
    }
}

/// <summary>The job a manual entry led to, and whether it was already known from an earlier sighting.</summary>
public sealed record ManualJobResult(Guid JobId, bool AlreadyKnown);

/// <summary>What an edit of a manual job did: saved, or refused with the reason and nothing changed.</summary>
public sealed record ManualJobEditResult(string? Refusal)
{
    /// <summary>True when the edit was saved.</summary>
    public bool IsSaved => Refusal is null;
}

/// <summary>Creates and edits the jobs entered by hand, through the same normalization, compensation and prefilter steps a fetched job goes through.</summary>
public sealed class ManualJobService(IDbContextFactory<JobHunterDbContext> contextFactory, SettingsService settingsService, Prefilter prefilter, CompNormalizer compNormalizer)
{
    /// <summary>Stores the job and leaves it unscored, so that the next score run scores it; a link that is already known is returned instead of stored twice.</summary>
    public async Task<ManualJobResult> CreateAsync(ManualJobRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        ManualEntry entry = ManualEntry.Read(request);

        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Job? known = await context.Jobs.FirstOrDefaultAsync(candidate => candidate.Fingerprint == entry.Fingerprint, cancellationToken);
        if (known is not null)
        {
            known.RecordSource(JobSourceKind.Manual, entry.CanonicalUrl, now);
            await context.SaveChangesAsync(cancellationToken);

            return new ManualJobResult(known.Id, true);
        }

        Domain.Settings settings = await settingsService.GetAsync(cancellationToken);

        Job job = Job.Create(
            entry.Fingerprint,
            entry.CanonicalUrl,
            entry.PostingUrl,
            entry.Company,
            entry.Title,
            entry.Description,
            entry.DescriptionHash,
            now,
            isManual: true);

        job.RecordSource(JobSourceKind.Manual, entry.CanonicalUrl, now);
        job.RecordPostingFacts(entry.PostingUrl, null, entry.Ats, [], null, null);
        job.RecordPlace(entry.LocationText, null, null, null, null);
        await ApplyCompensationAndPrefilterAsync(job, entry.Comp, settings, now, cancellationToken);

        context.Jobs.Add(job);
        await context.SaveChangesAsync(cancellationToken);

        return new ManualJobResult(job.Id, false);
    }

    /// <summary>Replaces what was typed for a manual job through the same steps as <see cref="CreateAsync"/>: a new description sends the job back for scoring the way a rewritten board posting does, and a job still scored is reclassified from its stored score card against the new pay the way the compensation recompute does, without calling any model.</summary>
    /// <remarks>Refuses without changing anything when the job came from a source, or when another job already holds the new link.</remarks>
    public async Task<ManualJobEditResult> UpdateAsync(Guid jobId, ManualJobRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        ManualEntry entry = ManualEntry.Read(request);

        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Job job = await context.Jobs.SingleAsync(candidate => candidate.Id == jobId, cancellationToken);
        if (!job.IsManual)
        {
            return new ManualJobEditResult("Only a job entered by hand can be edited; a source owns the facts of this one.");
        }

        bool linkTaken = await context.Jobs.AnyAsync(candidate => candidate.Fingerprint == entry.Fingerprint && candidate.Id != jobId, cancellationToken);
        if (linkTaken)
        {
            return new ManualJobEditResult("Another job already has that link.");
        }

        Domain.Settings settings = await settingsService.GetAsync(cancellationToken);

        job.ReviseManualEntry(entry.Fingerprint, entry.CanonicalUrl, entry.PostingUrl, entry.Ats, entry.Company, entry.Title, entry.LocationText);
        job.ReviseDescription(entry.Description, entry.DescriptionHash);
        await ApplyCompensationAndPrefilterAsync(job, entry.Comp, settings, now, cancellationToken);
        KeepWhatScoringDecided(job, settings);

        await context.SaveChangesAsync(cancellationToken);

        return new ManualJobEditResult(null);
    }

    /// <summary>Normalizes the typed pay to the base currency per year, then runs the prefilter, which for a manual job never drops and only raises the flags.</summary>
    private async Task ApplyCompensationAndPrefilterAsync(Job job, ManualComp comp, Domain.Settings settings, DateTimeOffset now, CancellationToken cancellationToken)
    {
        YearlyComp normalized = await compNormalizer.ToBasePerYearAsync(comp.Min, comp.Max, comp.Currency, comp.Period, settings.BaseCurrency, settings, cancellationToken);
        job.RecordCompensation(comp.Min, comp.Max, comp.Currency, comp.Period, normalized.MinPerYear, normalized.MaxPerYear, settings.HighPayThresholdPerYear);

        PrefilterVerdict verdict = prefilter.Evaluate(PrefilterInput.FromJob(job, settings, now));
        job.ApplyPrefilterVerdict(verdict.State, verdict.DropReason, verdict.Flags, settings.HighPayThresholdPerYear);
    }

    /// <summary>Puts back on a job that is still scored what its score card decided and the prefilter pass cleared: the work-authorization flag, and the class recomputed against the current pay.</summary>
    private static void KeepWhatScoringDecided(Job job, Domain.Settings settings)
    {
        if (job.Scoring != ScoringState.Scored || job.Score is not ScoreCard card)
        {
            return;
        }

        job.RecordWorkAuthorizationRequirement(card.RequiresUsAuthorization is true);
        CompRecomputeService.Reclassify(job, settings);
    }

    /// <summary>A manual entry once normalized: the link and the identity read from it, the plain-text description and its hash, and the pay read out of its free text.</summary>
    private sealed record ManualEntry(string PostingUrl, string CanonicalUrl, string Fingerprint, AtsKind? Ats, string Company, string Title, string Description, string DescriptionHash, string? LocationText, ManualComp Comp)
    {
        public static ManualEntry Read(ManualJobRequest request)
        {
            string postingUrl = request.ApplyUrl.Trim();
            string canonicalUrl = UrlCanonicalizer.Canonicalize(postingUrl);
            string description = ToPlainText(request.DescriptionText);

            return new ManualEntry(
                postingUrl,
                canonicalUrl,
                JobFingerprint.ForCanonicalUrl(canonicalUrl),
                AtsKindParser.Parse(null, postingUrl),
                request.Company.Trim(),
                request.Title.Trim(),
                description,
                JobFingerprint.ForDescription(description),
                Cleaned(request.LocationText),
                ManualCompParser.Parse(request.CompText));
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
}
