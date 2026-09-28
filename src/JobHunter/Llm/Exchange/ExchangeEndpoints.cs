using System.Text;
using System.Text.Json.Serialization;
using JobHunter.Applications;
using JobHunter.Llm.Contracts;
using JobHunter.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace JobHunter.Llm.Exchange;

/// <summary>One scored line as the scored endpoint reports it back.</summary>
public sealed record ScoredLineResponse(
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("job_id")] string? JobId,
    [property: JsonPropertyName("class")] string? Class,
    [property: JsonPropertyName("total")] int? Total,
    [property: JsonPropertyName("flags")] IReadOnlyList<string> Flags,
    [property: JsonPropertyName("refusal")] string? Refusal)
{
    /// <summary>Builds the response line from what the import did with the line.</summary>
    public static ScoredLineResponse From(ScoreLineOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return new ScoredLineResponse(outcome.LineNumber, outcome.JobId, outcome.Class?.ToString(), outcome.Total, outcome.Flags, outcome.Refusal);
    }
}

/// <summary>What the scored endpoint stored and refused, line by line.</summary>
public sealed record ScoredLinesResponse(
    [property: JsonPropertyName("imported")] int Imported,
    [property: JsonPropertyName("refused")] int Refused,
    [property: JsonPropertyName("lines")] IReadOnlyList<ScoredLineResponse> Lines);

/// <summary>What the new job endpoint stored or found, with the page that shows the job.</summary>
public sealed record NewJobResponse(
    [property: JsonPropertyName("job_id")] Guid? JobId,
    [property: JsonPropertyName("already_exists")] bool AlreadyExists,
    [property: JsonPropertyName("class")] string? Class,
    [property: JsonPropertyName("total")] int? Total,
    [property: JsonPropertyName("flags")] IReadOnlyList<string> Flags,
    [property: JsonPropertyName("job_page")] string? JobPage,
    [property: JsonPropertyName("refusal")] string? Refusal)
{
    /// <summary>Builds the response from what the import did with the line.</summary>
    public static NewJobResponse From(NewJobOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return new NewJobResponse(
            outcome.JobId,
            outcome.AlreadyExists,
            outcome.Class?.ToString(),
            outcome.Total,
            outcome.Flags,
            outcome.JobId is Guid jobId ? $"/jobs/{jobId}" : null,
            outcome.Refusal);
    }
}

/// <summary>Why the exchange could not hand out or take in what was asked.</summary>
public sealed record ExchangeRefusal([property: JsonPropertyName("refusal")] string Refusal);

/// <summary>What the cover-letter endpoint stored or refused: the job and its page with the lint findings, or the reason.</summary>
public sealed record CoverLetterResponse(
    [property: JsonPropertyName("job_id")] Guid? JobId,
    [property: JsonPropertyName("job_page")] string? JobPage,
    [property: JsonPropertyName("lint_issues")] IReadOnlyList<string> LintIssues,
    [property: JsonPropertyName("refusal")] string? Refusal)
{
    /// <summary>Builds the response from what the check-and-store step did with the letter.</summary>
    public static CoverLetterResponse From(CoverLetterStoreResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new CoverLetterResponse(result.JobId, result.JobId is Guid jobId ? $"/jobs/{jobId}" : null, result.LintIssues, result.Refusal);
    }
}

/// <summary>Maps the exchange over HTTP: the same to-score lines the export writes, and the same import the Import button runs, for a caller on this machine.</summary>
public static class ExchangeEndpoints
{
    /// <summary>The route that returns the jobs still waiting for a score, or the jobs named by repeated <c>job</c> query parameters.</summary>
    public const string ToScoreRoute = "/exchange/to-score";

    /// <summary>The route that imports scored lines.</summary>
    public const string ScoredRoute = "/exchange/scored";

    /// <summary>The route that stores a new job together with its score.</summary>
    public const string NewJobRoute = "/exchange/new-job";

    /// <summary>The route that returns the saved job named by the <c>job</c> query parameter, to write its cover letter from.</summary>
    public const string ToCoverRoute = "/exchange/to-cover";

    /// <summary>The route that stores one cover letter on its job's application.</summary>
    public const string CoverLetterRoute = "/exchange/cover-letter";

    /// <summary>Maps the five exchange endpoints.</summary>
    public static IEndpointRouteBuilder MapExchangeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(ToScoreRoute, GetJobsToScoreAsync);
        endpoints.MapPost(ScoredRoute, ImportScoredAsync);
        endpoints.MapPost(NewJobRoute, ImportNewJobAsync);
        endpoints.MapGet(ToCoverRoute, GetJobToCoverAsync);
        endpoints.MapPost(CoverLetterRoute, ImportCoverLetterAsync);

        return endpoints;
    }

    /// <summary>Returns to-score JSON lines: without job ids, every job still waiting for a score, the same lines the export writes to the to-score file; with job ids, exactly the named jobs that are stored, scored or not.</summary>
    public static async Task<ContentHttpResult> GetJobsToScoreAsync(ExchangeExporter exporter, [FromQuery(Name = "job")] string[]? jobIds, CancellationToken cancellationToken)
    {
        List<string> lines = jobIds is { Length: > 0 }
            ? await exporter.BuildScoreLinesForJobsAsync(ParseJobIds(jobIds), cancellationToken)
            : await exporter.BuildScoreLinesAsync(cancellationToken);
        string content = lines.Count == 0 ? string.Empty : string.Join('\n', lines) + '\n';

        return TypedResults.Text(content, "application/x-ndjson", Encoding.UTF8);
    }

    /// <summary>Imports the scored JSON lines in the request body through the exchange import and reports each line's class or refusal.</summary>
    public static async Task<Ok<ScoredLinesResponse>> ImportScoredAsync(HttpRequest request, ExchangeImporter importer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string body = await ReadBodyAsync(request, cancellationToken);
        IReadOnlyList<ScoreLineOutcome> outcomes = await importer.ImportScoreLinesAsync(body.Split('\n'), cancellationToken);
        int refused = outcomes.Count(outcome => outcome.Refusal is not null);

        return TypedResults.Ok(new ScoredLinesResponse(outcomes.Count - refused, refused, [.. outcomes.Select(ScoredLineResponse.From)]));
    }

    /// <summary>Stores the new job in the request body with its score: 201 for a new job, 200 for a link already held, 400 with the reason for a refused line.</summary>
    public static async Task<Results<Created<NewJobResponse>, Ok<NewJobResponse>, BadRequest<NewJobResponse>>> ImportNewJobAsync(HttpRequest request, NewJobImporter importer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string body = await ReadBodyAsync(request, cancellationToken);
        NewJobResponse response = NewJobResponse.From(await importer.ImportAsync(body, cancellationToken));

        if (response.JobId is null)
        {
            return TypedResults.BadRequest(response);
        }

        return response.AlreadyExists ? TypedResults.Ok(response) : TypedResults.Created(response.JobPage, response);
    }

    /// <summary>Returns the saved job a cover letter is to be written for: the kit's job input with the language hint and the resume, which is null when the app cannot read it; 404 for an id that names no stored job, 409 with the reason for a job that is not saved or not scored.</summary>
    public static async Task<Results<Ok<CoverLetterExchangeInput>, NotFound<ExchangeRefusal>, Conflict<ExchangeRefusal>>> GetJobToCoverAsync(
        CoverLetterService coverLetters,
        SettingsService settingsService,
        [FromQuery(Name = "job")] string? jobId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(jobId, out Guid id))
        {
            return TypedResults.NotFound(new ExchangeRefusal($"\"{jobId}\" is not a job identifier"));
        }

        CoverLetterJob found = await coverLetters.FindJobAsync(id, ExchangeFiles.Model, cancellationToken);
        if (found.Request is not KitRequest request)
        {
            ExchangeRefusal refusal = new(found.Refusal ?? "No cover letter can be written for this job.");

            return found.Block == CoverLetterBlock.UnknownJob ? TypedResults.NotFound(refusal) : TypedResults.Conflict(refusal);
        }

        Domain.Settings settings = await settingsService.GetAsync(cancellationToken);
        string? resume = await ResumeMarkdown.ReadAsync(settings.ResumeMarkdownPath, cancellationToken);

        return TypedResults.Ok(CoverLetterExchangeInput.From(request, resume));
    }

    /// <summary>Stores the cover letter in the request body through the same check the Write cover letter button uses: 201 with the job, its page and the lint findings, 400 with the reason for a refused letter.</summary>
    public static async Task<Results<Created<CoverLetterResponse>, BadRequest<CoverLetterResponse>>> ImportCoverLetterAsync(HttpRequest request, CoverLetterService coverLetters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        LlmJsonResult<CoverLetterPayload> parsed = LlmJson.Read<CoverLetterPayload>(await ReadBodyAsync(request, cancellationToken));
        if (parsed.Payload is not CoverLetterPayload payload)
        {
            return TypedResults.BadRequest(CoverLetterResponse.From(CoverLetterStoreResult.Refused($"the object does not match the cover letter schema: {parsed.Error}")));
        }

        CoverLetterResponse response = CoverLetterResponse.From(await coverLetters.StoreAsync(payload, ExchangeFiles.Model, null, cancellationToken));

        return response.Refusal is null ? TypedResults.Created(response.JobPage, response) : TypedResults.BadRequest(response);
    }

    /// <summary>The identifiers among the named jobs; a value that is not an identifier cannot name a stored job and is left out.</summary>
    private static List<Guid> ParseJobIds(IEnumerable<string> jobIds)
    {
        List<Guid> parsed = [];
        foreach (string jobId in jobIds)
        {
            if (Guid.TryParse(jobId, out Guid id))
            {
                parsed.Add(id);
            }
        }

        return parsed;
    }

    private static async Task<string> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using StreamReader reader = new(request.Body, Encoding.UTF8);

        return await reader.ReadToEndAsync(cancellationToken);
    }
}
