using System.Globalization;
using JobHunter.Domain;
using JobHunter.Llm;
using JobHunter.Refresh;
using Microsoft.AspNetCore.Components;

namespace JobHunter.Components.Shared;

/// <summary>The refresh panel above every page: it starts a refresh, shows what the run is doing while it runs, and reports what the last run changed.</summary>
public sealed partial class RefreshPanel : IDisposable
{
    private int jobsAwaitingScore;

    [Inject]
    private RefreshState State { get; set; } = null!;

    [Inject]
    private RefreshService Refresher { get; set; } = null!;

    [Inject]
    private ApiKeyDetector ApiKeys { get; set; } = null!;

    /// <summary>What a refused click left to say, shown next to the button until the next click.</summary>
    private string? Message { get; set; }

    private RefreshRunSummary? LastRun => State.LastRun;

    /// <summary>The per-source lines: the counters of the run in progress, or those of the last run once it finished.</summary>
    private IReadOnlyList<SourceRunResult> SourceLines => State.IsRunning || LastRun is null ? State.SourceResults : LastRun.SourceResults;

    private string? RunError => State.IsRunning ? null : LastRun?.Error;

    private bool ShowExportHint => jobsAwaitingScore > 0 && !ApiKeys.IsPresent;

    private static string ExportHint => ApiKeyDetector.MissingKeyMessage;

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

    protected override async Task OnInitializedAsync()
    {
        State.Changed += OnStateChanged;

        await LoadJobsAwaitingScoreAsync();
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
        await LoadJobsAwaitingScoreAsync();
    }

    private void OnStateChanged()
    {
        try
        {
            _ = InvokeAsync(RedrawAsync);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task RedrawAsync()
    {
        try
        {
            if (!State.IsRunning)
            {
                await LoadJobsAwaitingScoreAsync();
            }

            StateHasChanged();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task LoadJobsAwaitingScoreAsync()
    {
        jobsAwaitingScore = await Refresher.CountJobsAwaitingScoreAsync();
    }
}
