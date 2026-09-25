using System.Globalization;
using JobHunter.Domain;
using JobHunter.Llm;
using JobHunter.Refresh;
using Microsoft.AspNetCore.Components;

namespace JobHunter.Components.Shared;

/// <summary>The run panel above every page: it starts a refresh or a score run, shows what the run is doing while it runs, and reports what the last run changed.</summary>
/// <remarks>The Score button carries the count of jobs waiting for a score; the hint to export them when no key is configured belongs to the inbox.</remarks>
public sealed partial class RefreshPanel : IDisposable
{
    /// <summary>What the Score button says when it stays disabled for want of a key.</summary>
    private const string NoKeyTitle = "No API key: score through Export, the score-jobs skill and Import on the Inbox.";

    [Inject]
    private RefreshState State { get; set; } = null!;

    [Inject]
    private RefreshService Refresher { get; set; } = null!;

    [Inject]
    private ScoreRunService Scorer { get; set; } = null!;

    [Inject]
    private ScoreBacklog Backlog { get; set; } = null!;

    [Inject]
    private ApiKeyDetector KeyDetector { get; set; } = null!;

    [Inject]
    private ILogger<RefreshPanel> Logger { get; set; } = null!;

    /// <summary>What a refused click left to say, shown next to the buttons until the next click.</summary>
    private string? Message { get; set; }

    private RefreshRunSummary? LastRun => State.LastRun;

    /// <summary>The per-source lines: the counters of the run in progress, or those of the last run once it finished.</summary>
    private IReadOnlyList<SourceRunResult> SourceLines => State.IsRunning || LastRun is null ? State.SourceResults : LastRun.SourceResults;

    private string? RunError => State.IsRunning ? null : LastRun?.Error;

    /// <summary>Why the last run stopped scoring before every selected job was sent, shown until the next run starts.</summary>
    private string? ScoringHaltReason => State.IsRunning ? null : LastRun?.ScoringHaltReason;

    /// <summary>The Score button waits while any run is in progress, while nothing waits for a score and while no key is configured.</summary>
    private bool ScoreDisabled => State.IsRunning || State.AwaitingScore is not > 0 || !KeyDetector.IsPresent;

    private string? ScoreTitle => KeyDetector.IsPresent ? null : NoKeyTitle;

    private string AwaitingText => State.AwaitingScore is int waiting ? $"({waiting.ToString(CultureInfo.CurrentCulture)} waiting)" : string.Empty;

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

    /// <summary>Follows the live state and counts the jobs waiting for a score, so that the Score button is right from the first render of the circuit.</summary>
    protected override async Task OnInitializedAsync()
    {
        State.Changed += OnStateChanged;

        await Backlog.RecountAsync();
    }

    private static string FinishedAtText(RefreshRunSummary summary)
    {
        return summary.FinishedAt is DateTimeOffset finishedAt
            ? $" at {finishedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)}"
            : string.Empty;
    }

    private async Task RefreshAsync()
    {
        Message = null;

        RefreshResult result = await Refresher.RunAsync(FetchTrigger.Manual, CancellationToken.None);

        Message = result.Refusal;
    }

    private async Task ScoreAsync()
    {
        Message = null;

        RefreshResult result = await Scorer.RunAsync(CancellationToken.None);

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
