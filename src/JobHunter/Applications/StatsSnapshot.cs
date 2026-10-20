using JobHunter.Domain;
using JobHunter.Sources;

namespace JobHunter.Applications;

/// <summary>How many applications reached Applied in one ISO week.</summary>
public sealed record WeeklyApplicationCount(int IsoYear, int IsoWeek, DateOnly WeekStart, int Count);

/// <summary>A step of the application funnel: how far along an application got.</summary>
public enum FunnelStage
{
    /// <summary>Submitted: reached Applied or any later status, closed ones included.</summary>
    Applied,

    /// <summary>Reached Screening or any later round.</summary>
    Screening,

    /// <summary>Reached an interview round (Manager, Tech, System design or Fit) or an offer.</summary>
    Interview,

    /// <summary>Reached an offer.</summary>
    Offer,

    /// <summary>Accepted an offer.</summary>
    Accepted
}

/// <summary>How many applications reached one funnel stage at some point, whatever their status now.</summary>
public sealed record FunnelStep(FunnelStage Stage, int Count);

/// <summary>Everything the statistics page shows, computed from the current applications and jobs, as of one moment.</summary>
public sealed record StatsSnapshot(
    IReadOnlyDictionary<ApplicationStatus, int> StatusCounts,
    IReadOnlyList<FunnelStep> Funnel,
    IReadOnlyList<WeeklyApplicationCount> ApplicationsPerWeek,
    double? ResponseRate,
    double? MedianDaysToFirstReply,
    IReadOnlyDictionary<JobSourceKind, int> BySource,
    IReadOnlyDictionary<AtsKind, int> ByAts,
    int PursuedNotAppliedCount,
    int FollowUpsDueCount,
    int FollowUpsOverdueCount,
    int InactiveWithOpenApplicationCount);
