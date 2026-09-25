using System.Globalization;
using System.Text.Json.Serialization;
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
