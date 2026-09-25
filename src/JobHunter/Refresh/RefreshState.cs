using JobHunter.Domain;

namespace JobHunter.Refresh;

/// <summary>The step a run is working on; a score run has the scoring step alone.</summary>
public enum RefreshPhase
{
    Idle,
    Fetching,
    Merging,
    Liveness,
    Scoring
}

/// <summary>One immutable picture of the runs: what the run in progress is doing now, what the sources have contributed so far, the run that finished last, and how many jobs wait for a score once they have been counted.</summary>
public sealed record RefreshStatus(
    bool IsRunning,
    RefreshPhase Phase,
    string? Activity,
    IReadOnlyList<SourceRunResult> SourceResults,
    IReadOnlyList<string> Errors,
    int Scored,
    int ScoreFailures,
    int ScoreTarget,
    RefreshRunSummary? LastRun,
    int? AwaitingScore)
{
    /// <summary>The picture while nothing is running, nothing has run yet and nothing has been counted.</summary>
    public static RefreshStatus Idle { get; } = new(false, RefreshPhase.Idle, null, [], [], 0, 0, 0, null, null);
}

/// <summary>The live state of the refresh and of the score run, shared by the whole application: the panel reads it and redraws whenever it changes.</summary>
/// <remarks>Every change replaces the whole picture under a lock, so a reader never sees half of one, and the event is raised outside the lock.</remarks>
public sealed class RefreshState
{
    private readonly Lock gate = new();

    private RefreshStatus status = RefreshStatus.Idle;

    /// <summary>Raised after every change, on whichever thread made it.</summary>
    public event Action? Changed;

    /// <summary>The whole picture as one value.</summary>
    public RefreshStatus Status => status;

    /// <summary>True while a run is in progress.</summary>
    public bool IsRunning => status.IsRunning;

    /// <summary>The step the run is on.</summary>
    public RefreshPhase Phase => status.Phase;

    /// <summary>What the run is doing right now, in words.</summary>
    public string? Activity => status.Activity;

    /// <summary>What each source has contributed to the run in progress.</summary>
    public IReadOnlyList<SourceRunResult> SourceResults => status.SourceResults;

    /// <summary>The problems reported during the run in progress.</summary>
    public IReadOnlyList<string> Errors => status.Errors;

    /// <summary>How many jobs have been scored in the run in progress.</summary>
    public int Scored => status.Scored;

    /// <summary>How many scoring calls failed in the run in progress.</summary>
    public int ScoreFailures => status.ScoreFailures;

    /// <summary>How many jobs the run in progress set out to score.</summary>
    public int ScoreTarget => status.ScoreTarget;

    /// <summary>The run that finished last, whether it completed or failed.</summary>
    public RefreshRunSummary? LastRun => status.LastRun;

    /// <summary>How many jobs wait for a score, or null until they have been counted.</summary>
    public int? AwaitingScore => status.AwaitingScore;

    /// <summary>Opens a run: the counters of the previous one make way for the new ones while its summary stays on screen; a score run starts on the scoring step, a refresh on fetching.</summary>
    public void BeginRun(FetchTrigger trigger)
    {
        bool isScoreRun = trigger == FetchTrigger.Score;

        Update(current => current with
        {
            IsRunning = true,
            Phase = isScoreRun ? RefreshPhase.Scoring : RefreshPhase.Fetching,
            Activity = isScoreRun ? "score run started" : $"{trigger.ToString().ToLowerInvariant()} refresh started",
            SourceResults = [],
            Errors = [],
            Scored = 0,
            ScoreFailures = 0,
            ScoreTarget = 0
        });
    }

    /// <summary>Moves the run to another step.</summary>
    public void EnterPhase(RefreshPhase phase, string? activity)
    {
        Update(current => current with { Phase = phase, Activity = activity });
    }

    /// <summary>Records what one source contributed, adding an error line when it reported a problem.</summary>
    public void RecordSourceResult(SourceRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        Update(current => current with
        {
            SourceResults = [.. current.SourceResults, result],
            Errors = result.Error is null ? current.Errors : [.. current.Errors, $"{result.Kind}: {result.Error}"]
        });
    }

    /// <summary>Moves the run to scoring and says how many jobs it will send.</summary>
    public void BeginScoring(int target, string? activity)
    {
        Update(current => current with { Phase = RefreshPhase.Scoring, ScoreTarget = target, Activity = activity });
    }

    /// <summary>Records how far scoring has come.</summary>
    public void RecordScoreProgress(int scored, int scoreFailures)
    {
        Update(current => current with { Scored = scored, ScoreFailures = scoreFailures });
    }

    /// <summary>Records how many jobs wait for a score.</summary>
    public void RecordAwaitingScore(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        Update(current => current with { AwaitingScore = count });
    }

    /// <summary>Puts the summary of the run that finished last back on the panel after a restart; a run of this process already on screen is left alone.</summary>
    public void RestoreLastRun(RefreshRunSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        Update(current => current.LastRun is null && !current.IsRunning ? current with { LastRun = summary } : current);
    }

    /// <summary>Closes the run and keeps its summary for the panel.</summary>
    public void CompleteRun(RefreshRunSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        Update(current => current with { IsRunning = false, Phase = RefreshPhase.Idle, Activity = null, LastRun = summary });
    }

    private void Update(Func<RefreshStatus, RefreshStatus> change)
    {
        lock (gate)
        {
            status = change(status);
        }

        Changed?.Invoke();
    }
}
