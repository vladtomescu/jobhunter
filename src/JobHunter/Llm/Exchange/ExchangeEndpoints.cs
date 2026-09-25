using System.Text;
using System.Text.Json.Serialization;
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

/// <summary>Maps the exchange over HTTP: the same to-score lines the export writes, and the same import the Import button runs, for a caller on this machine.</summary>
public static class ExchangeEndpoints
{
    /// <summary>The route that returns the jobs still waiting for a score, or the jobs named by repeated <c>job</c> query parameters.</summary>
    public const string ToScoreRoute = "/exchange/to-score";

    /// <summary>The route that imports scored lines.</summary>
    public const string ScoredRoute = "/exchange/scored";

    /// <summary>The route that stores a new job together with its score.</summary>
    public const string NewJobRoute = "/exchange/new-job";

    /// <summary>Maps the three exchange endpoints.</summary>
    public static IEndpointRouteBuilder MapExchangeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(ToScoreRoute, GetJobsToScoreAsync);
        endpoints.MapPost(ScoredRoute, ImportScoredAsync);
        endpoints.MapPost(NewJobRoute, ImportNewJobAsync);

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
