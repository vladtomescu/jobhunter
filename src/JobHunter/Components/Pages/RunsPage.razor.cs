using System.Globalization;
using JobHunter.Domain;
using JobHunter.Refresh;
using Microsoft.AspNetCore.Components;

namespace JobHunter.Components.Pages;

/// <summary>The run history: every refresh with its counts, and on demand what each source returned and why scoring calls failed.</summary>
/// <remarks>The table is hand-written markup because QuickGrid renders exactly one row per item and cannot host the expandable detail row; the file is named RunsPage so that its class never shadows a namespace segment.</remarks>
public sealed partial class RunsPage : IDisposable
{
    private readonly HashSet<Guid> expandedRunIds = [];

    private IReadOnlyList<RefreshRunSummary>? runs;

    private Guid? lastRunShown;

    [Inject]
    private RunHistoryService RunHistory { get; set; } = null!;

    [Inject]
    private RefreshState State { get; set; } = null!;

    [Inject]
    private ILogger<RunsPage> Logger { get; set; } = null!;

    /// <summary>Stops following the live refresh state so that a closed circuit is no longer reloaded.</summary>
    public void Dispose()
    {
        State.Changed -= OnStateChanged;
    }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        lastRunShown = State.LastRun?.RunId;
        State.Changed += OnStateChanged;

        await LoadAsync();
    }

    private static string DetailKey(Guid runId)
    {
        return $"detail-{runId}";
    }

    private static string DurationText(RefreshRunSummary run)
    {
        if (run.Duration is not TimeSpan duration)
        {
            return run.Outcome == FetchOutcome.Running ? "running" : string.Empty;
        }

        return duration.TotalMinutes >= 1
            ? string.Create(CultureInfo.CurrentCulture, $"{(int)duration.TotalMinutes} min {duration.Seconds} s")
            : string.Create(CultureInfo.CurrentCulture, $"{duration.TotalSeconds:0.#} s");
    }

    private static string OutcomeRowClass(RefreshRunSummary run)
    {
        return run.Outcome switch
        {
            FetchOutcome.Failed => "run-failed",
            FetchOutcome.Running => "run-running",
            _ => run.ScoringHaltReason is null ? string.Empty : "run-halted"
        };
    }

    private async Task LoadAsync()
    {
        runs = await RunHistory.GetRunsAsync();
    }

    private void Toggle(Guid runId)
    {
        if (!expandedRunIds.Remove(runId))
        {
            expandedRunIds.Add(runId);
        }
    }

    /// <summary>Reloads the history when a run finishes while the page is open; the change arrives on the refresh's thread, so the reload is marshalled onto the circuit.</summary>
    private void OnStateChanged()
    {
        try
        {
            _ = InvokeAsync(ReloadWhenARunFinishedAsync).ContinueWith(ReportReloadFailure, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
        catch (ObjectDisposedException exception)
        {
            Logger.LogDebug(exception, "A refresh state change reached the runs page after its circuit had closed.");
        }
    }

    private async Task ReloadWhenARunFinishedAsync()
    {
        Guid? finished = State.LastRun?.RunId;

        if (State.IsRunning || finished == lastRunShown)
        {
            return;
        }

        lastRunShown = finished;
        await LoadAsync();
        StateHasChanged();
    }

    private void ReportReloadFailure(Task reload)
    {
        Exception? failure = reload.Exception?.GetBaseException();

        if (failure is ObjectDisposedException)
        {
            Logger.LogDebug(failure, "The runs page was reloaded after its circuit had closed.");

            return;
        }

        Logger.LogWarning(failure, "The runs page could not be reloaded after a run finished.");
    }
}
