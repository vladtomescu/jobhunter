using System.Text.RegularExpressions;
using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Llm;
using JobHunter.Llm.Contracts;
using JobHunter.Pipeline;
using JobHunter.Settings;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Applications;

/// <summary>Why no cover letter can be written for a job.</summary>
public enum CoverLetterBlock
{
    /// <summary>No job is stored under the identifier.</summary>
    UnknownJob,

    /// <summary>The job has no application, and a letter is kept on the application.</summary>
    NotSaved,

    /// <summary>The job carries no score, and the letter is written from the score facts.</summary>
    NotScored
}

/// <summary>What the cover letter for one job is written from, or why none can be written.</summary>
public sealed record CoverLetterJob(KitRequest? Request, CoverLetterBlock? Block, string? Refusal)
{
    /// <summary>A job a letter cannot be written for, with the reason.</summary>
    public static CoverLetterJob Blocked(CoverLetterBlock block, string refusal)
    {
        return new CoverLetterJob(null, block, refusal);
    }
}

/// <summary>What the check-and-store step did with one letter: the job it is now stored on and what the lint found, or the reason it was refused and nothing was stored.</summary>
public sealed record CoverLetterStoreResult(Guid? JobId, IReadOnlyList<string> LintIssues, string? Refusal)
{
    /// <summary>True when the letter was stored.</summary>
    public bool IsStored => Refusal is null;

    /// <summary>A letter that stored nothing, with the reason.</summary>
    public static CoverLetterStoreResult Refused(string reason)
    {
        return new CoverLetterStoreResult(null, [], reason);
    }
}

/// <summary>Writes cover letters through the model and stores letters from the model and from the exchange through one check both paths share.</summary>
/// <remarks>A letter is written from the same job input as the kit, so only a saved and scored job can take one; a stored letter keeps its lint findings with it, as a kit does, and a rewrite replaces it.</remarks>
public sealed partial class CoverLetterService(IDbContextFactory<JobHunterDbContext> contextFactory, ICoverLetterWriter coverLetterWriter, SettingsService settingsService)
{
    /// <summary>The fewest paragraphs a letter may have.</summary>
    public const int MinParagraphs = 3;

    /// <summary>The most paragraphs a letter may have.</summary>
    public const int MaxParagraphs = 7;

    /// <summary>The job input a letter for the job is written from, carrying the given model; refused for an unknown job, a job that is not saved and a job without a score.</summary>
    public async Task<CoverLetterJob> FindJobAsync(Guid jobId, string model, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Job? job = await context.Jobs.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == jobId, cancellationToken);
        if (job is null)
        {
            return CoverLetterJob.Blocked(CoverLetterBlock.UnknownJob, UnknownJobReason(jobId));
        }

        if (!await context.Applications.AnyAsync(application => application.JobId == jobId, cancellationToken))
        {
            return CoverLetterJob.Blocked(CoverLetterBlock.NotSaved, NotSavedReason(jobId));
        }

        if (job.Score is not ScoreCard score)
        {
            return CoverLetterJob.Blocked(CoverLetterBlock.NotScored, $"the job {jobId} has not been scored yet, and a cover letter is written from its score facts; score it first");
        }

        return new CoverLetterJob(LlmRequests.ForKit(job, ScoreCardMapper.ToScorePayload(jobId, score), model), null, null);
    }

    /// <summary>Writes the letter for the job through the kit model and stores it through the same check the exchange uses; a job that cannot take a letter, or a model that produced none, comes back as the reason.</summary>
    public async Task<CoverLetterStoreResult> WriteAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        Domain.Settings settings = await settingsService.GetAsync(cancellationToken);
        CoverLetterJob found = await FindJobAsync(jobId, settings.KitModel, cancellationToken);
        if (found.Request is not KitRequest request)
        {
            return CoverLetterStoreResult.Refused(found.Refusal ?? "No cover letter can be written for this job.");
        }

        CoverLetterOutcome outcome;
        try
        {
            outcome = await coverLetterWriter.WriteAsync(request, cancellationToken);
        }
        catch (Exception exception)
        {
            return CoverLetterStoreResult.Refused(exception.Message);
        }

        if (outcome.Payload is not CoverLetterPayload payload)
        {
            return CoverLetterStoreResult.Refused(outcome.FailureReason ?? "The cover letter writer returned no usable result.");
        }

        return await StoreAsync(payload, outcome.Model ?? settings.KitModel, jobId, cancellationToken);
    }

    /// <summary>The one check-and-store step both paths share: refuses a malformed or mismatched job id, a letter outside the contract, an unknown job and a job that is not saved, then lints the letter and stores it on the job's application in place of any earlier one.</summary>
    /// <param name="payload">The letter as the schema defines it.</param>
    /// <param name="model">The model recorded on the stored letter.</param>
    /// <param name="writtenForJobId">The job the letter was asked for, when the caller knows it; a letter naming another job is refused.</param>
    /// <param name="cancellationToken">Cancels the store.</param>
    public async Task<CoverLetterStoreResult> StoreAsync(CoverLetterPayload payload, string model, Guid? writtenForJobId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        if (!Guid.TryParse(payload.JobId, out Guid jobId))
        {
            return CoverLetterStoreResult.Refused($"job_id {payload.JobId} is not an identifier");
        }

        if (writtenForJobId is Guid expectedJobId && expectedJobId != jobId)
        {
            return CoverLetterStoreResult.Refused($"job_id {jobId} does not match the job {expectedJobId} the letter was written for");
        }

        Domain.Settings settings = await settingsService.GetAsync(cancellationToken);
        if (OutOfContract(payload, settings) is string problem)
        {
            return CoverLetterStoreResult.Refused(problem);
        }

        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        if (!await context.Jobs.AnyAsync(job => job.Id == jobId, cancellationToken))
        {
            return CoverLetterStoreResult.Refused(UnknownJobReason(jobId));
        }

        Application? application = await context.Applications.FirstOrDefaultAsync(candidate => candidate.JobId == jobId, cancellationToken);
        if (application is null)
        {
            return CoverLetterStoreResult.Refused(NotSavedReason(jobId));
        }

        IReadOnlyList<string> issues = KitLint.Inspect(payload);
        application.AttachCoverLetter(new ApplicationCoverLetter(
            payload.Language,
            payload.Salutation.Trim(),
            [.. payload.Paragraphs.Select(paragraph => paragraph.Trim())],
            payload.Closing.Trim(),
            DateTimeOffset.UtcNow,
            model,
            [.. issues]));
        await context.SaveChangesAsync(cancellationToken);

        return new CoverLetterStoreResult(jobId, issues, null);
    }

    /// <summary>The reason a letter falls outside the contract the schema and the instructions set, or null when it fits.</summary>
    private static string? OutOfContract(CoverLetterPayload payload, Domain.Settings settings)
    {
        if (!LanguageCode().IsMatch(payload.Language))
        {
            return $"language \"{payload.Language}\" is not a two-letter lower-case ISO 639-1 code";
        }

        IReadOnlySet<string> acceptedLanguages = CandidateProfile.ReadLanguages(settings.AcceptedLanguages);
        if (!acceptedLanguages.Contains(payload.Language))
        {
            return $"the language {payload.Language} is not one of the accepted languages: {string.Join(", ", acceptedLanguages)}";
        }

        if (string.IsNullOrWhiteSpace(payload.Salutation))
        {
            return "salutation is empty";
        }

        if (string.IsNullOrWhiteSpace(payload.Closing))
        {
            return "closing is empty";
        }

        if (payload.Paragraphs.Count is < MinParagraphs or > MaxParagraphs)
        {
            return $"the letter has {payload.Paragraphs.Count} paragraphs, and it needs {MinParagraphs} to {MaxParagraphs}";
        }

        int blank = payload.Paragraphs.FindIndex(string.IsNullOrWhiteSpace);

        return blank < 0 ? null : $"paragraphs[{blank}] is blank";
    }

    private static string UnknownJobReason(Guid jobId)
    {
        return $"no job is stored under the identifier {jobId}";
    }

    private static string NotSavedReason(Guid jobId)
    {
        return $"the job {jobId} is not saved, and a cover letter is kept on the application a save creates; save the job first";
    }

    [GeneratedRegex("^[a-z]{2}$")]
    private static partial Regex LanguageCode();
}
