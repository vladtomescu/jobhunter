using System.Text.RegularExpressions;
using JobHunter.Sources;

namespace JobHunter.Domain;

/// <summary>What one source contributed to a refresh run, or the error it failed with.</summary>
public sealed record SourceRunResult(JobSourceKind Kind, int Fetched, int New, int Updated, int Dropped, string? Error);

/// <summary>One reason scoring calls failed in a run, and how many calls failed with it.</summary>
public sealed record ScoringFailureReason(string Reason, int Count);

/// <summary>One run, a refresh or a score run: what each source returned and what the liveness pass changed on a refresh, how much was scored and why scoring calls failed on a score run.</summary>
public sealed partial class FetchRun
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

    /// <summary>The failed scoring calls grouped by reason, the most frequent first; empty for runs stored before the reasons were kept.</summary>
    public List<ScoringFailureReason> ScoringFailureReasons { get; private set; } = [];

    /// <summary>Why scoring stopped before every selected job was sent, or null when it ran to the end.</summary>
    public string? ScoringHaltReason { get; private set; }

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

    /// <summary>Records how many jobs were scored and the reason of every failed scoring call, grouped so that failures differing only in a request identifier count as one reason.</summary>
    public void RecordScoring(int scored, IReadOnlyList<string> failureReasons)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(scored);
        ArgumentNullException.ThrowIfNull(failureReasons);

        Scored = scored;
        ScoreFailures = failureReasons.Count;
        ScoringFailureReasons = [.. failureReasons
            .Select(NormalizeFailureReason)
            .GroupBy(reason => reason, StringComparer.Ordinal)
            .Select(group => new ScoringFailureReason(group.Key, group.Count()))
            .OrderByDescending(group => group.Count)
            .ThenBy(group => group.Reason, StringComparer.Ordinal)];
    }

    /// <summary>Records why scoring stopped before every selected job was sent; the first reason stands, because the calls still in flight can report the same stop again.</summary>
    public void HaltScoring(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        ScoringHaltReason ??= NormalizeFailureReason(reason);
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

    /// <summary>A failure reason without the per-request identifiers the API adds and with its whitespace collapsed, so identical failures read as one line.</summary>
    private static string NormalizeFailureReason(string reason)
    {
        string withoutRequestIds = RequestIdPattern().Replace(reason, string.Empty);
        string collapsed = WhitespacePattern().Replace(withoutRequestIds, " ").Trim();

        return collapsed.Length == 0 ? "no reason given" : collapsed;
    }

    [GeneratedRegex("""(,\s*)?"request_id"\s*:\s*"[^"]*"|\breq_[A-Za-z0-9]+""")]
    private static partial Regex RequestIdPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
