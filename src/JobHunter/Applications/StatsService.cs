using System.Globalization;
using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Sources;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Applications;

/// <summary>Computes how the search is going from the current applications and jobs; nothing here changes any record.</summary>
public sealed class StatsService(IDbContextFactory<JobHunterDbContext> contextFactory)
{
    private const int TrailingWeekCount = 12;

    private static readonly IReadOnlyCollection<ApplicationStatus> ScreeningOrLaterStatuses =
    [
        ApplicationStatus.Screening,
        ApplicationStatus.Interview1,
        ApplicationStatus.Interview2,
        ApplicationStatus.Final,
        ApplicationStatus.Offer,
        ApplicationStatus.Accepted
    ];

    private static readonly IReadOnlyCollection<ApplicationStatus> AppliedOrLaterStatuses = [ApplicationStatus.Applied, .. ScreeningOrLaterStatuses];

    private static readonly IReadOnlyCollection<ApplicationStatus> TerminalStatuses =
    [
        ApplicationStatus.Accepted,
        ApplicationStatus.Rejected,
        ApplicationStatus.Withdrawn,
        ApplicationStatus.Ghosted
    ];

    /// <summary>Builds the stats snapshot as of asOf.</summary>
    public async Task<StatsSnapshot> GetSnapshotAsync(DateTimeOffset asOf, CancellationToken cancellationToken = default)
    {
        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        List<Application> applications = await context.Applications.AsNoTracking().ToListAsync(cancellationToken);
        Dictionary<Guid, AppliedJobFacts> jobById = await ReadJobFactsAsync(context, applications, cancellationToken);

        DateOnly today = DateOnly.FromDateTime(asOf.UtcDateTime);

        return new StatsSnapshot(
            BuildStatusCounts(applications),
            BuildWeeklyCounts(applications, asOf),
            ComputeResponseRate(applications),
            ComputeMedianDaysToFirstReply(applications),
            BuildBySource(applications, jobById),
            BuildByAts(applications, jobById),
            applications.Count(application => application.Status == ApplicationStatus.Saved),
            applications.Count(application => application.NextActionDue == today),
            applications.Count(application => application.NextActionDue is DateOnly due && due < today),
            applications.Count(application => IsOpen(application) && jobById.TryGetValue(application.JobId, out AppliedJobFacts? job) && !job.IsActive));
    }

    private static async Task<Dictionary<Guid, AppliedJobFacts>> ReadJobFactsAsync(JobHunterDbContext context, IReadOnlyList<Application> applications, CancellationToken cancellationToken)
    {
        List<Guid> jobIds = [.. applications.Select(application => application.JobId).Distinct()];
        if (jobIds.Count == 0)
        {
            return [];
        }

        List<AppliedJobFacts> facts = await context.Jobs
            .AsNoTracking()
            .Where(job => jobIds.Contains(job.Id))
            .Select(job => new AppliedJobFacts(job.Id, job.IsActive, job.Ats, job.Sources))
            .ToListAsync(cancellationToken);

        return facts.ToDictionary(job => job.Id);
    }

    private static bool IsOpen(Application application)
    {
        return !TerminalStatuses.Contains(application.Status);
    }

    private static IReadOnlyDictionary<ApplicationStatus, int> BuildStatusCounts(IReadOnlyList<Application> applications)
    {
        Dictionary<ApplicationStatus, int> counts = Enum.GetValues<ApplicationStatus>().ToDictionary(status => status, _ => 0);

        foreach (Application application in applications)
        {
            counts[application.Status]++;
        }

        return counts;
    }

    private static IReadOnlyList<WeeklyApplicationCount> BuildWeeklyCounts(IReadOnlyList<Application> applications, DateTimeOffset asOf)
    {
        DateOnly currentWeekStart = StartOfIsoWeek(DateOnly.FromDateTime(asOf.UtcDateTime));
        List<WeeklyApplicationCount> weeks = [];

        for (int weeksAgo = TrailingWeekCount - 1; weeksAgo >= 0; weeksAgo--)
        {
            DateOnly weekStart = currentWeekStart.AddDays(-7 * weeksAgo);
            DateOnly weekEnd = weekStart.AddDays(6);
            DateTime weekStartDateTime = weekStart.ToDateTime(TimeOnly.MinValue);

            int count = applications.Count(application =>
                application.AppliedAt is DateTimeOffset appliedAt &&
                DateOnly.FromDateTime(appliedAt.UtcDateTime) >= weekStart &&
                DateOnly.FromDateTime(appliedAt.UtcDateTime) <= weekEnd);

            weeks.Add(new WeeklyApplicationCount(ISOWeek.GetYear(weekStartDateTime), ISOWeek.GetWeekOfYear(weekStartDateTime), weekStart, count));
        }

        return weeks;
    }

    private static DateOnly StartOfIsoWeek(DateOnly date)
    {
        DateTime dateTime = date.ToDateTime(TimeOnly.MinValue);

        return DateOnly.FromDateTime(ISOWeek.ToDateTime(ISOWeek.GetYear(dateTime), ISOWeek.GetWeekOfYear(dateTime), DayOfWeek.Monday));
    }

    private static double? ComputeResponseRate(IReadOnlyList<Application> applications)
    {
        int reachedAppliedOrLater = applications.Count(application => application.History.Any(entry => AppliedOrLaterStatuses.Contains(entry.Status)));
        if (reachedAppliedOrLater == 0)
        {
            return null;
        }

        int reachedScreeningOrLater = applications.Count(application => application.History.Any(entry => ScreeningOrLaterStatuses.Contains(entry.Status)));

        return (double)reachedScreeningOrLater / reachedAppliedOrLater;
    }

    private static double? ComputeMedianDaysToFirstReply(IReadOnlyList<Application> applications)
    {
        List<double> daysToFirstReply = [];

        foreach (Application application in applications)
        {
            List<ApplicationHistoryEntry> history = [.. application.History.OrderBy(entry => entry.At)];
            int appliedIndex = history.FindIndex(entry => entry.Status == ApplicationStatus.Applied);
            if (appliedIndex < 0 || appliedIndex + 1 >= history.Count)
            {
                continue;
            }

            daysToFirstReply.Add((history[appliedIndex + 1].At - history[appliedIndex].At).TotalDays);
        }

        if (daysToFirstReply.Count == 0)
        {
            return null;
        }

        daysToFirstReply.Sort();
        int middle = daysToFirstReply.Count / 2;

        return daysToFirstReply.Count % 2 == 0
            ? (daysToFirstReply[middle - 1] + daysToFirstReply[middle]) / 2.0
            : daysToFirstReply[middle];
    }

    private static IReadOnlyDictionary<JobSourceKind, int> BuildBySource(IReadOnlyList<Application> applications, IReadOnlyDictionary<Guid, AppliedJobFacts> jobById)
    {
        Dictionary<JobSourceKind, int> counts = [];

        foreach (Application application in applications)
        {
            if (!jobById.TryGetValue(application.JobId, out AppliedJobFacts? job))
            {
                continue;
            }

            foreach (JobSourceKind kind in job.Sources.Select(source => source.Kind).Distinct())
            {
                counts[kind] = counts.GetValueOrDefault(kind) + 1;
            }
        }

        return counts;
    }

    private static IReadOnlyDictionary<AtsKind, int> BuildByAts(IReadOnlyList<Application> applications, IReadOnlyDictionary<Guid, AppliedJobFacts> jobById)
    {
        Dictionary<AtsKind, int> counts = [];

        foreach (Application application in applications)
        {
            if (jobById.TryGetValue(application.JobId, out AppliedJobFacts? job) && job.Ats is AtsKind ats)
            {
                counts[ats] = counts.GetValueOrDefault(ats) + 1;
            }
        }

        return counts;
    }

    /// <summary>The handful of job facts the statistics read, so that descriptions and score cards never travel with them.</summary>
    private sealed record AppliedJobFacts(Guid Id, bool IsActive, AtsKind? Ats, List<JobSourceRef> Sources);
}
