using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Jobs;
using JobHunter.Llm.Contracts;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Llm.Exchange;

/// <summary>What became of one new job line: the job it stored or found and the class, total and flags that job holds, or the reason the line was refused.</summary>
public sealed record NewJobOutcome(Guid? JobId, bool AlreadyExists, JobClass? Class, int? Total, IReadOnlyList<string> Flags, string? Refusal)
{
    /// <summary>A line that stored nothing, with the reason.</summary>
    public static NewJobOutcome Refused(string reason)
    {
        return new NewJobOutcome(null, false, null, null, [], reason);
    }
}

/// <summary>Stores a job no source covers together with the score it arrived with: the job goes through the manual add path and the score through the exchange import, so both are handled exactly as they are everywhere else.</summary>
public sealed class NewJobImporter(ManualJobService manualJobService, ExchangeImporter exchangeImporter, IDbContextFactory<JobHunterDbContext> contextFactory)
{
    /// <summary>Checks the whole line before anything is stored, then stores the job and imports its score; a link the application already holds stores nothing and reports the job it already has.</summary>
    public async Task<NewJobOutcome> ImportAsync(string line, CancellationToken cancellationToken = default)
    {
        LlmJsonResult<NewJobExchangeLine> parsed = LlmJson.Read<NewJobExchangeLine>(line);
        if (parsed.Payload is not NewJobExchangeLine newJob)
        {
            return NewJobOutcome.Refused($"the line does not match the new job schema: {parsed.Error}");
        }

        if (CheckJob(newJob) is string jobProblem)
        {
            return NewJobOutcome.Refused(jobProblem);
        }

        if (ExchangeImporter.CheckScore(newJob.Score) is string scoreProblem)
        {
            return NewJobOutcome.Refused(scoreProblem);
        }

        ManualJobResult stored = await manualJobService.CreateAsync(
            new ManualJobRequest(newJob.ApplyUrl, newJob.Title, newJob.Company, newJob.Description, newJob.Location, newJob.CompText),
            cancellationToken);

        if (stored.AlreadyKnown)
        {
            return await DescribeExistingAsync(stored.JobId, cancellationToken);
        }

        ScorePayload score = newJob.Score with { JobId = stored.JobId.ToString() };
        IReadOnlyList<ScoreLineOutcome> imported = await exchangeImporter.ImportScoreLinesAsync([LlmJson.ToLine(score)], cancellationToken);
        ScoreLineOutcome outcome = imported[0];

        return new NewJobOutcome(stored.JobId, false, outcome.Class, outcome.Total, outcome.Flags, outcome.Refusal);
    }

    private static string? CheckJob(NewJobExchangeLine newJob)
    {
        if (!Uri.TryCreate(newJob.ApplyUrl.Trim(), UriKind.Absolute, out Uri? link) || (link.Scheme != Uri.UriSchemeHttp && link.Scheme != Uri.UriSchemeHttps))
        {
            return $"apply_url \"{newJob.ApplyUrl}\" is not an absolute http or https link";
        }

        (string Name, string Value)[] required = [("title", newJob.Title), ("company", newJob.Company), ("description", newJob.Description)];
        foreach ((string name, string value) in required)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return $"{name} is empty";
            }
        }

        return newJob.Score.JobId.Length == 0 ? null : "score.job_id must be empty, because the application assigns the identifier when it stores the job";
    }

    private async Task<NewJobOutcome> DescribeExistingAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        Job existing = await context.Jobs.AsNoTracking().SingleAsync(job => job.Id == jobId, cancellationToken);

        return new NewJobOutcome(existing.Id, true, existing.Class, existing.Score?.Total, [.. existing.Flags.Select(flag => flag.ToString())], null);
    }
}
