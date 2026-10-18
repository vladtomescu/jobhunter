using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
public static partial class ExchangeEndpoints
{
    /// <summary>The route that returns the jobs still waiting for a score, a claimed batch of them with the <c>take</c> query parameter, or the jobs named by repeated <c>job</c> query parameters.</summary>
    public const string ToScoreRoute = "/exchange/to-score";

    /// <summary>The route that imports scored lines.</summary>
    public const string ScoredRoute = "/exchange/scored";

    /// <summary>The route that stores a new job together with its score.</summary>
    public const string NewJobRoute = "/exchange/new-job";

    /// <summary>The route that returns the saved job named by the <c>job</c> query parameter, to write its cover letter from.</summary>
    public const string ToCoverRoute = "/exchange/to-cover";

    /// <summary>The route that stores one cover letter on its job's application.</summary>
    public const string CoverLetterRoute = "/exchange/cover-letter";

    /// <summary>The route that returns the stored job named by the <c>job</c> query parameter, to prepare an interview round from.</summary>
    public const string ToPrepRoute = "/exchange/to-prep";

    /// <summary>The route that returns the resume markdown the scoring skill matches skills against.</summary>
    public const string ResumeRoute = "/exchange/resume";

    /// <summary>Maps the seven exchange endpoints.</summary>
    public static IEndpointRouteBuilder MapExchangeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(ToScoreRoute, GetJobsToScoreAsync);
        endpoints.MapGet(ResumeRoute, GetResumeAsync);
        endpoints.MapPost(ScoredRoute, ImportScoredAsync);
        endpoints.MapPost(NewJobRoute, ImportNewJobAsync);
        endpoints.MapGet(ToCoverRoute, GetJobToCoverAsync);
        endpoints.MapPost(CoverLetterRoute, ImportCoverLetterAsync);
        endpoints.MapGet(ToPrepRoute, GetJobToPrepAsync);

        return endpoints;
    }

    /// <summary>Returns to-score JSON lines: with job ids, exactly the named jobs that are stored, scored or not, whatever <c>take</c> says; with <c>take</c>, at most that many jobs scored without skills, or waiting for a score, that no earlier batch holds, the jobs scored without skills first, each group in scoring order, each job claimed for 30 minutes or until its score is imported; with neither, every scored job without skills and then every job still waiting for a score, in scoring order, the same lines the export writes to the to-score file. Only <c>take</c> claims, and a <c>take</c> that is not a positive whole number answers 400.</summary>
    public static async Task<Results<ContentHttpResult, BadRequest<ExchangeRefusal>>> GetJobsToScoreAsync(
        ExchangeExporter exporter,
        [FromQuery(Name = "job")] string[]? jobIds,
        [FromQuery(Name = "take")] string? take,
        CancellationToken cancellationToken)
    {
        List<string> lines;
        if (jobIds is { Length: > 0 })
        {
            lines = await exporter.BuildScoreLinesForJobsAsync(ParseJobIds(jobIds), cancellationToken);
        }
        else if (take is null)
        {
            lines = await exporter.BuildScoreLinesAsync(cancellationToken);
        }
        else if (int.TryParse(take, NumberStyles.None, CultureInfo.InvariantCulture, out int count) && count > 0)
        {
            lines = await exporter.ClaimScoreLinesAsync(count, cancellationToken);
        }
        else
        {
            return TypedResults.BadRequest(new ExchangeRefusal($"take is \"{take}\", which is not a positive whole number"));
        }

        string content = lines.Count == 0 ? string.Empty : string.Join('\n', lines) + '\n';

        return TypedResults.Text(content, "application/x-ndjson", Encoding.UTF8);
    }

    /// <summary>Imports the scored JSON lines in the request body through the exchange import and reports each line's class or refusal; the scores record the <c>model</c> query parameter as their model, or the repository skill without it, and a model that is not 1 to 64 letters, digits, dots or dashes answers 400 and imports nothing.</summary>
    public static async Task<Results<Ok<ScoredLinesResponse>, BadRequest<ExchangeRefusal>>> ImportScoredAsync(
        HttpRequest request,
        ExchangeImporter importer,
        [FromQuery(Name = "model")] string? model,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (model is not null && !ModelIdentifier().IsMatch(model))
        {
            return TypedResults.BadRequest(new ExchangeRefusal($"model is \"{model}\", which is not a model identifier: 1 to 64 letters, digits, dots or dashes"));
        }

        string body = await ReadBodyAsync(request, cancellationToken);
        IReadOnlyList<ScoreLineOutcome> outcomes = await importer.ImportScoreLinesAsync(body.Split('\n'), model ?? ExchangeFiles.Model, cancellationToken);
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
            return TypedResults.NotFound(NotAJobIdentifier(jobId));
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

    /// <summary>Returns everything a private interview prep for one stored job is written from, whatever the job's triage, score or application status: the posting, its score and class or null, the resume or null, the application or null, and the pay settings; 404 for an id that names no stored job. Nothing is changed.</summary>
    public static async Task<Results<Ok<PrepExchangeInput>, NotFound<ExchangeRefusal>>> GetJobToPrepAsync(ExchangeExporter exporter, [FromQuery(Name = "job")] string? jobId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(jobId, out Guid id))
        {
            return TypedResults.NotFound(NotAJobIdentifier(jobId));
        }

        PrepExchangeInput? input = await exporter.BuildPrepInputAsync(id, cancellationToken);

        return input is null ? TypedResults.NotFound(new ExchangeRefusal($"no job is stored under the identifier {id}")) : TypedResults.Ok(input);
    }

    /// <summary>Returns the resume markdown the settings point at, the same text the scoring call puts in its prompt; 404 with the reason when no path is saved or the file cannot be read.</summary>
    public static async Task<Results<ContentHttpResult, NotFound<ExchangeRefusal>>> GetResumeAsync(SettingsService settingsService, CancellationToken cancellationToken)
    {
        Domain.Settings settings = await settingsService.GetAsync(cancellationToken);
        string? resume = await ResumeMarkdown.ReadAsync(settings.ResumeMarkdownPath, cancellationToken);

        return resume is null
            ? TypedResults.NotFound(new ExchangeRefusal("no resume markdown can be read: set its path in the settings"))
            : TypedResults.Text(resume, "text/markdown", Encoding.UTF8);
    }

    private static ExchangeRefusal NotAJobIdentifier(string? jobId)
    {
        return new ExchangeRefusal($"\"{jobId}\" is not a job identifier");
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

    /// <summary>A model identifier as a score records it, such as <c>claude-haiku-5-5</c>; <c>\z</c> rather than <c>$</c>, so a trailing line break is refused too.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9.\-]{1,64}\z")]
    private static partial Regex ModelIdentifier();
}
