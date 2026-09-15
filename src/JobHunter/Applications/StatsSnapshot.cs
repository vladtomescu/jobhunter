using JobHunter.Domain;
using JobHunter.Sources;

namespace JobHunter.Applications;

/// <summary>How many applications reached Applied in one ISO week.</summary>
public sealed record WeeklyApplicationCount(int IsoYear, int IsoWeek, DateOnly WeekStart, int Count);

/// <summary>Everything the statistics page shows, computed from the current applications and jobs, as of one moment.</summary>
public sealed record StatsSnapshot(
    IReadOnlyDictionary<ApplicationStatus, int> StatusCounts,
    IReadOnlyList<WeeklyApplicationCount> ApplicationsPerWeek,
    double? ResponseRate,
    double? MedianDaysToFirstReply,
    IReadOnlyDictionary<JobSourceKind, int> BySource,
    IReadOnlyDictionary<AtsKind, int> ByAts,
    int PursuedNotAppliedCount,
    int FollowUpsDueCount,
    int FollowUpsOverdueCount,
    int InactiveWithOpenApplicationCount);
