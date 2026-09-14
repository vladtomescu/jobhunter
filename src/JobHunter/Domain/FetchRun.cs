using JobHunter.Sources;

namespace JobHunter.Domain;

/// <summary>What one source contributed to a refresh run, or the error it failed with.</summary>
public sealed record SourceRunResult(JobSourceKind Kind, int Fetched, int New, int Updated, int Dropped, string? Error);

/// <summary>One refresh run: what each source returned, how much was scored, and what the liveness pass changed.</summary>
public sealed class FetchRun
{
    private FetchRun()
    {
    }

    public Guid Id { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? FinishedAt { get; private set; }

    public FetchTrigger Trigger { get; private set; }

    public FetchOutcome Outcome { get; private set; }

    public List<SourceRunResult> SourceResults { get; private set; } = [];

    public int Scored { get; private set; }

    public int ScoreFailures { get; private set; }

    public int MarkedInactive { get; private set; }

    public int MarkedStale { get; private set; }

    public string? Error { get; private set; }

    /// <summary>Opens a run record; it stays in the Running outcome until the run finishes or fails.</summary>
    public static FetchRun Start(FetchTrigger trigger, DateTimeOffset startedAt)
    {
        return new FetchRun
        {
            Id = Guid.CreateVersion7(),
            Trigger = trigger,
            StartedAt = startedAt,
            Outcome = FetchOutcome.Running
        };
    }

    /// <summary>Records what one source returned, including the error line when it failed.</summary>
    public void RecordSourceResult(JobSourceKind kind, int fetched, int added, int updated, int dropped, string? error)
    {
        SourceResults.Add(new SourceRunResult(kind, fetched, added, updated, dropped, error));
    }

    /// <summary>Records how many jobs were scored and how many scoring calls failed.</summary>
    public void RecordScoring(int scored, int scoreFailures)
    {
        Scored = scored;
        ScoreFailures = scoreFailures;
    }

    /// <summary>Records what the liveness and aging passes changed.</summary>
    public void RecordLiveness(int markedInactive, int markedStale)
    {
        MarkedInactive = markedInactive;
        MarkedStale = markedStale;
    }

    /// <summary>Closes the run as completed.</summary>
    public void Complete(DateTimeOffset finishedAt)
    {
        Outcome = FetchOutcome.Completed;
        FinishedAt = finishedAt;
    }

    /// <summary>Closes the run as failed, with the error that stopped it.</summary>
    public void Fail(string error, DateTimeOffset finishedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        Outcome = FetchOutcome.Failed;
        Error = error;
        FinishedAt = finishedAt;
    }
}
