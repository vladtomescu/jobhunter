using System.Globalization;
using JobHunter.Domain;
using JobHunter.Refresh;
using Microsoft.AspNetCore.Components;

namespace JobHunter.Components.Shared;

/// <summary>The refresh panel above every page: it starts a refresh, shows what the run is doing while it runs, and reports what the last run changed.</summary>
/// <remarks>The panel reports runs only; the count of jobs waiting for a score and the hint to export them belong to the inbox.</remarks>
public sealed partial class RefreshPanel : IDisposable
{
    [Inject]
    private RefreshState State { get; set; } = null!;

    [Inject]
    private RefreshService Refresher { get; set; } = null!;

    [Inject]
    private ILogger<RefreshPanel> Logger { get; set; } = null!;

    /// <summary>What a refused click left to say, shown next to the button until the next click.</summary>
    private string? Message { get; set; }

    private RefreshRunSummary? LastRun => State.LastRun;

    /// <summary>The per-source lines: the counters of the run in progress, or those of the last run once it finished.</summary>
    private IReadOnlyList<SourceRunResult> SourceLines => State.IsRunning || LastRun is null ? State.SourceResults : LastRun.SourceResults;

    private string? RunError => State.IsRunning ? null : LastRun?.Error;

    /// <summary>Why the last run stopped scoring before every selected job was sent, shown until the next run starts.</summary>
    private string? ScoringHaltReason => State.IsRunning ? null : LastRun?.ScoringHaltReason;

    private string RunningText
    {
        get
        {
            string phase = State.Phase.ToString().ToLowerInvariant();
            string activity = State.Activity is string words ? $": {words}" : string.Empty;
            string scoring = State.Phase == RefreshPhase.Scoring && State.ScoreTarget > 0
                ? $" ({State.Scored} scored, {State.ScoreFailures} failed of {State.ScoreTarget})"
                : string.Empty;

            return $"{phase}{activity}{scoring}";
        }
    }

    /// <summary>Unsubscribes from the live state so that a closed circuit stops being redrawn.</summary>
    public void Dispose()
    {
        State.Changed -= OnStateChanged;
    }

    protected override void OnInitialized()
    {
        State.Changed += OnStateChanged;
    }

    private static string FinishedAtText(RefreshRunSummary summary)
    {
        return summary.FinishedAt is DateTimeOffset finishedAt
            ? $" at {finishedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)}"
            : string.Empty;
    }

    private async Task RunAsync()
    {
        Message = null;

        RefreshResult result = await Refresher.RunAsync(FetchTrigger.Manual, CancellationToken.None);

        Message = result.Refusal;
    }

    /// <summary>Marshals the redraw onto the circuit; the call is not awaited, so its outcome is observed by a continuation instead of being dropped.</summary>
    private void OnStateChanged()
    {
        try
        {
            _ = InvokeAsync(StateHasChanged).ContinueWith(ReportRedrawFailure, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
        catch (ObjectDisposedException exception)
        {
            Logger.LogDebug(exception, "A refresh state change reached the panel after its circuit had closed.");
        }
    }

    private void ReportRedrawFailure(Task redraw)
    {
        Exception? failure = redraw.Exception?.GetBaseException();

        if (failure is ObjectDisposedException)
        {
            Logger.LogDebug(failure, "The panel was redrawn after its circuit had closed.");

            return;
        }

        Logger.LogWarning(failure, "The refresh panel could not be redrawn after a refresh state change.");
    }
}
