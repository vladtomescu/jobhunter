using JobHunter.Domain;

namespace JobHunter.Refresh;

/// <summary>What a refresh run left behind, read off the stored run record so that the interface never holds on to a database entity.</summary>
public sealed record RefreshRunSummary(
    Guid RunId,
    FetchTrigger Trigger,
    FetchOutcome Outcome,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<SourceRunResult> SourceResults,
    int Scored,
    int ScoreFailures,
    IReadOnlyList<ScoringFailureReason> ScoringFailureReasons,
    string? ScoringHaltReason,
    int MarkedInactive,
    int MarkedStale,
    string? Error)
{
    /// <summary>Reads the summary off a run record.</summary>
    public static RefreshRunSummary FromRun(FetchRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new RefreshRunSummary(
            run.Id,
            run.Trigger,
            run.Outcome,
            run.StartedAt,
            run.FinishedAt,
            [.. run.SourceResults],
            run.Scored,
            run.ScoreFailures,
            [.. run.ScoringFailureReasons],
            run.ScoringHaltReason,
            run.MarkedInactive,
            run.MarkedStale,
            run.Error);
    }

    /// <summary>How long the run took, or null while it has not finished.</summary>
    public TimeSpan? Duration => FinishedAt is DateTimeOffset finishedAt ? finishedAt - StartedAt : null;

    /// <summary>How many postings the sources added between them.</summary>
    public int NewJobs => SourceResults.Sum(result => result.New);

    /// <summary>How many postings the sources refreshed between them.</summary>
    public int UpdatedJobs => SourceResults.Sum(result => result.Updated);

    /// <summary>One line per source that reported a problem, source named first, followed by the error that stopped the run when there was one.</summary>
    public IReadOnlyList<string> ErrorLines
    {
        get
        {
            List<string> lines = [.. SourceResults.Where(result => result.Error is not null).Select(result => $"{result.Kind}: {result.Error}")];

            if (Error is not null)
            {
                lines.Add(Error);
            }

            return lines;
        }
    }
}
