using System.Globalization;
using JobHunter.Applications;
using JobHunter.Domain;
using JobHunter.Jobs;
using JobHunter.Sources;
using Microsoft.AspNetCore.Components;

namespace JobHunter.Components.Pages;

/// <summary>Code-behind for the stats page: every metric the stats service returns, as QuickGrid tables with CSS bars for the ratio and the weekly counts.</summary>
public sealed partial class Stats : ComponentBase
{
    [Inject]
    private StatsService StatsService { get; set; } = null!;

    private StatsSnapshot? snapshot;

    private IQueryable<StatusCountRow> statusRows = Array.Empty<StatusCountRow>().AsQueryable();

    private IQueryable<WeeklyRow> weeklyRows = Array.Empty<WeeklyRow>().AsQueryable();

    private IQueryable<SourceCountRow> sourceRows = Array.Empty<SourceCountRow>().AsQueryable();

    private IQueryable<AtsCountRow> atsRows = Array.Empty<AtsCountRow>().AsQueryable();

    private IQueryable<FollowUpRow> followUpRows = Array.Empty<FollowUpRow>().AsQueryable();

    private string responseRateBarWidth = "0%";

    private IReadOnlyList<FunnelRow> funnelRows = [];

    private int totalApplied;

    /// <inheritdoc/>
    protected override async Task OnInitializedAsync()
    {
        StatsSnapshot loaded = await StatsService.GetSnapshotAsync(DateTimeOffset.UtcNow);

        statusRows = BuildStatusRows(loaded);
        weeklyRows = BuildWeeklyRows(loaded);
        sourceRows = BuildSourceRows(loaded);
        atsRows = BuildAtsRows(loaded);
        followUpRows = new FollowUpRow[] { new(loaded.FollowUpsDueCount, loaded.FollowUpsOverdueCount) }.AsQueryable();
        responseRateBarWidth = BarWidth(loaded.ResponseRate ?? 0);
        funnelRows = BuildFunnelRows(loaded);
        totalApplied = loaded.Funnel.Count == 0 ? 0 : loaded.Funnel[0].Count;

        snapshot = loaded;
    }

    private static IQueryable<StatusCountRow> BuildStatusRows(StatsSnapshot snapshot)
    {
        StatusCountRow[] rows = [.. snapshot.StatusCounts.Select(entry => new StatusCountRow(JobDisplay.StatusLabel(entry.Key), entry.Value))];

        return rows.AsQueryable();
    }

    /// <summary>The funnel stages as bars sized against the first stage, each with its share of the stage before; the first stage, and one after a stage nobody reached, has no share.</summary>
    private static IReadOnlyList<FunnelRow> BuildFunnelRows(StatsSnapshot snapshot)
    {
        int first = snapshot.Funnel.Count == 0 ? 0 : snapshot.Funnel[0].Count;

        return
        [
            .. snapshot.Funnel.Select((step, index) => new FunnelRow(
                FunnelLabel(step.Stage),
                step.Count,
                index == 0 || snapshot.Funnel[index - 1].Count == 0 ? string.Empty : $"({FormatPercent((double)step.Count / snapshot.Funnel[index - 1].Count)})",
                BarWidth(step.Count, first)))
        ];
    }

    private static string FunnelLabel(FunnelStage stage)
    {
        return stage switch
        {
            FunnelStage.Applied => "Applied",
            FunnelStage.Screening => "Screening",
            FunnelStage.Interview => "Interview rounds",
            FunnelStage.Offer => "Offer",
            FunnelStage.Accepted => "Accepted",
            _ => stage.ToString()
        };
    }

    private static IQueryable<WeeklyRow> BuildWeeklyRows(StatsSnapshot snapshot)
    {
        int maxCount = snapshot.ApplicationsPerWeek.Count == 0 ? 0 : snapshot.ApplicationsPerWeek.Max(week => week.Count);
        WeeklyRow[] rows =
        [
            .. snapshot.ApplicationsPerWeek.Select(week => new WeeklyRow(FormatWeekLabel(week), week.Count, BarWidth(week.Count, maxCount)))
        ];

        return rows.AsQueryable();
    }

    private static IQueryable<SourceCountRow> BuildSourceRows(StatsSnapshot snapshot)
    {
        SourceCountRow[] rows = [.. snapshot.BySource.Select(entry => new SourceCountRow(entry.Key, entry.Value))];

        return rows.AsQueryable();
    }

    private static IQueryable<AtsCountRow> BuildAtsRows(StatsSnapshot snapshot)
    {
        AtsCountRow[] rows = [.. snapshot.ByAts.Select(entry => new AtsCountRow(entry.Key, entry.Value))];

        return rows.AsQueryable();
    }

    private static string BarWidth(int value, int max)
    {
        return max <= 0 ? "0%" : string.Create(CultureInfo.InvariantCulture, $"{value * 100.0 / max:0.#}%");
    }

    private static string BarWidth(double ratio)
    {
        double clamped = Math.Clamp(ratio, 0, 1);

        return string.Create(CultureInfo.InvariantCulture, $"{clamped * 100.0:0.#}%");
    }

    private static string FormatWeekLabel(WeeklyApplicationCount week)
    {
        return $"{week.IsoYear}-W{week.IsoWeek:00} (from {week.WeekStart:yyyy-MM-dd})";
    }

    private static string FormatDays(double? days)
    {
        return days is double value ? $"{value:0.#} days" : "no data yet";
    }

    private static string FormatPercent(double? ratio)
    {
        return ratio is double value ? $"{value * 100:0.#}%" : "no data yet";
    }

    /// <summary>One row of the counts-per-status grid, with the status in the words the pages use.</summary>
    private sealed record StatusCountRow(string Status, int Count);

    /// <summary>One stage of the funnel: its label, how many reached it, its share of the stage before and the width its bar gets.</summary>
    private sealed record FunnelRow(string Label, int Count, string Share, string BarWidth);

    /// <summary>One week of the applications-per-week grid, with the width its volume bar gets.</summary>
    private sealed record WeeklyRow(string Label, int Count, string BarWidth);

    /// <summary>One row of the applications-by-source grid.</summary>
    private sealed record SourceCountRow(JobSourceKind Source, int Count);

    /// <summary>One row of the applications-by-ATS grid.</summary>
    private sealed record AtsCountRow(AtsKind Ats, int Count);

    /// <summary>The single row of the follow-ups grid: how many actions fall due today and how many are past due.</summary>
    private sealed record FollowUpRow(int DueToday, int Overdue);
}
