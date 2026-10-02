using System.Globalization;
using System.Text.Json.Serialization;
using JobHunter.Applications;
using JobHunter.Domain;
using JobHunter.Llm.Contracts;

namespace JobHunter.Llm.Exchange;

/// <summary>The names of the files the application and the repository skills hand to each other inside the exchange folder.</summary>
public static class ExchangeFiles
{
    /// <summary>Jobs waiting to be scored, written by the export.</summary>
    public const string ToScore = "to_score.jsonl";

    /// <summary>Scores written by the scoring skill, read by the import.</summary>
    public const string Scored = "scored.jsonl";

    /// <summary>Pursued jobs waiting for a kit, written by the export.</summary>
    public const string ToKit = "to_kit.jsonl";

    /// <summary>Kits written by the kit skill, read by the import.</summary>
    public const string Kits = "kits.jsonl";

    /// <summary>The resume the kit skill writes from, copied by the export.</summary>
    public const string Resume = "resume.md";

    /// <summary>The model name recorded on everything that came back through these files.</summary>
    public const string Model = "claude-code";
}

/// <summary>One job as it leaves for scoring, and as the user turn of a scoring call.</summary>
public sealed record ScoreExchangeLine(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("company")] string Company,
    [property: JsonPropertyName("location")] string Location,
    [property: JsonPropertyName("remote_hint")] string RemoteHint,
    [property: JsonPropertyName("employment_hint")] string EmploymentHint,
    [property: JsonPropertyName("comp_text")] string CompText,
    [property: JsonPropertyName("posted_at")] string PostedAt,
    [property: JsonPropertyName("flags")] List<string> Flags,
    [property: JsonPropertyName("description")] string Description)
{
    /// <summary>Builds the line from the request the scorer was given.</summary>
    public static ScoreExchangeLine From(ScoreRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ScoreExchangeLine(
            request.JobId.ToString(),
            request.Title,
            request.Company,
            request.Location ?? string.Empty,
            request.RemoteHint ?? string.Empty,
            request.EmploymentHint ?? string.Empty,
            request.CompText ?? string.Empty,
            request.PostedAt?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            [.. request.Flags],
            LlmRequests.Truncate(request.Description));
    }
}

/// <summary>One pursued job as it leaves for a kit, and as the user turn of a kit call.</summary>
public sealed record KitExchangeLine(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("company")] string Company,
    [property: JsonPropertyName("apply_url")] string ApplyUrl,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("score")] ScorePayload Score,
    [property: JsonPropertyName("class")] string Class,
    [property: JsonPropertyName("flags")] List<string> Flags,
    [property: JsonPropertyName("language_hint")] string LanguageHint)
{
    /// <summary>Builds the line from the request the kit writer was given.</summary>
    public static KitExchangeLine From(KitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new KitExchangeLine(
            request.JobId.ToString(),
            request.Title,
            request.Company,
            request.ApplyUrl ?? string.Empty,
            LlmRequests.Truncate(request.Description),
            request.Score,
            request.Class,
            [.. request.Flags],
            request.LanguageHint ?? string.Empty);
    }
}

/// <summary>One saved job as it leaves for a cover letter: the job input the kit is written from, and the resume the letter may draw facts from.</summary>
public sealed record CoverLetterExchangeInput(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("company")] string Company,
    [property: JsonPropertyName("apply_url")] string ApplyUrl,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("score")] ScorePayload Score,
    [property: JsonPropertyName("class")] string Class,
    [property: JsonPropertyName("flags")] List<string> Flags,
    [property: JsonPropertyName("language_hint")] string LanguageHint,
    [property: JsonPropertyName("resume")] string? Resume)
{
    /// <summary>Builds the input from the request a cover letter is written from and the resume markdown, which is null when the app cannot read it.</summary>
    public static CoverLetterExchangeInput From(KitRequest request, string? resumeMarkdown)
    {
        KitExchangeLine job = KitExchangeLine.From(request);

        return new CoverLetterExchangeInput(job.JobId, job.Title, job.Company, job.ApplyUrl, job.Description, job.Score, job.Class, job.Flags, job.LanguageHint, resumeMarkdown);
    }
}

/// <summary>One stored job as it leaves for a private interview prep: the posting with its whole description, the score and class when it has them, the resume, the application with everything the company already has, and the pay settings, which no other model is given.</summary>
public sealed record PrepExchangeInput(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("company")] string Company,
    [property: JsonPropertyName("location")] string Location,
    [property: JsonPropertyName("remote_hint")] string RemoteHint,
    [property: JsonPropertyName("employment_hint")] string EmploymentHint,
    [property: JsonPropertyName("comp_text")] string CompText,
    [property: JsonPropertyName("apply_url")] string ApplyUrl,
    [property: JsonPropertyName("posted_at")] string PostedAt,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("score")] ScorePayload? Score,
    [property: JsonPropertyName("class")] string? Class,
    [property: JsonPropertyName("flags")] List<string> Flags,
    [property: JsonPropertyName("resume")] string? Resume,
    [property: JsonPropertyName("application")] PrepApplication? Application,
    [property: JsonPropertyName("pay")] PrepPay Pay)
{
    /// <summary>Builds the input from the stored job, with the posting fields the to-score line carries; the score and class are null for a job without a score, the application is null for a job without one, and the resume is null when the app cannot read it.</summary>
    public static PrepExchangeInput From(Job job, Application? application, Domain.Settings settings, string? resumeMarkdown)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(settings);

        ScoreExchangeLine posting = ScoreExchangeLine.From(LlmRequests.ForScore(job, ExchangeFiles.Model));

        return new PrepExchangeInput(
            posting.JobId,
            posting.Title,
            posting.Company,
            posting.Location,
            posting.RemoteHint,
            posting.EmploymentHint,
            posting.CompText,
            job.ApplyUrl ?? job.PostingUrl,
            posting.PostedAt,
            job.DescriptionText,
            job.Score is ScoreCard score ? ScoreCardMapper.ToScorePayload(job.Id, score) : null,
            job.Score is null ? null : job.Class?.ToString(),
            posting.Flags,
            resumeMarkdown,
            application is null ? null : PrepApplication.From(application),
            PrepPay.From(settings));
    }
}

/// <summary>The application as the interview prep reads it: where it stands, how it got there, and what the company already has from the candidate.</summary>
public sealed record PrepApplication(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("status_changed_at")] string StatusChangedAt,
    [property: JsonPropertyName("applied_at")] string? AppliedAt,
    [property: JsonPropertyName("channel")] string? Channel,
    [property: JsonPropertyName("history")] List<PrepHistoryEntry> History,
    [property: JsonPropertyName("notes")] List<PrepNote> Notes,
    [property: JsonPropertyName("contact")] PrepContact? Contact,
    [property: JsonPropertyName("next_action")] string? NextAction,
    [property: JsonPropertyName("next_action_due")] string? NextActionDue,
    [property: JsonPropertyName("comp_discussed")] string? CompDiscussed,
    [property: JsonPropertyName("kit")] PrepKit? Kit,
    [property: JsonPropertyName("cover_letter")] PrepCoverLetter? CoverLetter)
{
    /// <summary>Builds the application line, naming each status by its stored name and writing each timestamp as round-trip text.</summary>
    public static PrepApplication From(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);

        return new PrepApplication(
            application.Status.ToString(),
            RoundTrip(application.StatusChangedAt),
            application.AppliedAt is DateTimeOffset appliedAt ? RoundTrip(appliedAt) : null,
            application.Channel?.ToString(),
            [.. application.History.Select(entry => new PrepHistoryEntry(entry.Status.ToString(), RoundTrip(entry.At), entry.Note))],
            [.. application.Notes.Select(note => new PrepNote(RoundTrip(note.At), note.Text))],
            application.Contact is ApplicationContact contact ? new PrepContact(contact.Name, contact.Role, contact.Link) : null,
            application.NextAction,
            application.NextActionDue?.ToString("O", CultureInfo.InvariantCulture),
            application.CompDiscussed,
            application.Kit is ApplicationKit kit ? KitFrom(kit) : null,
            application.CoverLetter is ApplicationCoverLetter letter ? new PrepCoverLetter(letter.Language, letter.Salutation, letter.Paragraphs, letter.Closing, RoundTrip(letter.WrittenAt)) : null);
    }

    private static PrepKit KitFrom(ApplicationKit kit)
    {
        return new PrepKit(
            kit.FitSummary,
            kit.CoverNote,
            kit.CallQuestions,
            [.. kit.AtsAnswers.Select(answer => new KitAnswerPayload(answer.Question, answer.Answer))],
            kit.Language,
            RoundTrip(kit.GeneratedAt));
    }

    private static string RoundTrip(DateTimeOffset at)
    {
        return at.ToString("O", CultureInfo.InvariantCulture);
    }
}

/// <summary>One status change of the application, with when it happened and the note written with it.</summary>
public sealed record PrepHistoryEntry(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("at")] string At,
    [property: JsonPropertyName("note")] string? Note);

/// <summary>One note on the application, such as what an earlier round said.</summary>
public sealed record PrepNote(
    [property: JsonPropertyName("at")] string At,
    [property: JsonPropertyName("text")] string Text);

/// <summary>The person on the company's side of the application.</summary>
public sealed record PrepContact(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("link")] string? Link);

/// <summary>The kit the company may already have read: the fit summary, the cover note, the standard answers and the questions for the first call.</summary>
public sealed record PrepKit(
    [property: JsonPropertyName("fit_summary")] List<string> FitSummary,
    [property: JsonPropertyName("cover_note")] string CoverNote,
    [property: JsonPropertyName("call_questions")] List<string> CallQuestions,
    [property: JsonPropertyName("ats_answers")] List<KitAnswerPayload> AtsAnswers,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("generated_at")] string GeneratedAt);

/// <summary>The body of the cover letter the company may already have read.</summary>
public sealed record PrepCoverLetter(
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("salutation")] string Salutation,
    [property: JsonPropertyName("paragraphs")] List<string> Paragraphs,
    [property: JsonPropertyName("closing")] string Closing,
    [property: JsonPropertyName("written_at")] string WrittenAt);

/// <summary>The compensation settings the private prep works the pay line out from, with the currency the amounts are counted in; null where a value is not set.</summary>
public sealed record PrepPay(
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("min_employment_annual")] decimal? MinEmploymentAnnual,
    [property: JsonPropertyName("min_contractor_hourly")] decimal? MinContractorHourly,
    [property: JsonPropertyName("target_annual")] decimal? TargetAnnual,
    [property: JsonPropertyName("contract_preference")] string ContractPreference,
    [property: JsonPropertyName("home_city")] string? HomeCity,
    [property: JsonPropertyName("home_country")] string? HomeCountry)
{
    /// <summary>Builds the pay line from the settings; the amounts are counted in the currency the stored pay was last computed in, which is the base currency except between a change of base currency and the recompute.</summary>
    public static PrepPay From(Domain.Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new PrepPay(
            settings.CompComputedInCurrency,
            settings.MinEmploymentAnnual,
            settings.MinContractorHourly,
            settings.TargetAnnual,
            settings.ContractPreference.ToString(),
            string.IsNullOrWhiteSpace(settings.HomeCity) ? null : settings.HomeCity,
            settings.HomeCountryIso);
    }
}

/// <summary>A job no source covers, arriving together with the score it was given before it was stored, as <c>prompts/schemas/new_job.schema.json</c> defines it.</summary>
/// <remarks>The score's <c>job_id</c> stays empty, because the job has no identifier until the application stores it.</remarks>
public sealed record NewJobExchangeLine(
    [property: JsonPropertyName("apply_url")] string ApplyUrl,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("company")] string Company,
    [property: JsonPropertyName("location")] string Location,
    [property: JsonPropertyName("comp_text")] string CompText,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("score")] ScorePayload Score);
