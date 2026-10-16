using JobHunter.Domain;

namespace JobHunter.Applications;

/// <summary>One stage of the job page's stepper: the status, the first time the application reached it (null when it has not yet) and whether it is the current one.</summary>
public sealed record ProgressStep(ApplicationStatus Status, DateTimeOffset? ReachedAt, bool IsCurrent);

/// <summary>Builds the stepper the job page draws over an application's history: the forward stages it reached, then the forward stages still ahead, then the closing status when it ended.</summary>
public static class ApplicationProgress
{
    /// <summary>The stages an application moves forward through, in the order the enum declares them.</summary>
    private static readonly ApplicationStatus[] ForwardStages =
    [
        ApplicationStatus.Saved,
        ApplicationStatus.Applied,
        ApplicationStatus.Screening,
        ApplicationStatus.Manager,
        ApplicationStatus.Tech,
        ApplicationStatus.SystemDesign,
        ApplicationStatus.Fit,
        ApplicationStatus.Offer,
        ApplicationStatus.Accepted
    ];

    /// <summary>The steps for an application with this history and current status; a stage it skipped is left out, so the stepper shows the road taken and the road ahead.</summary>
    public static IReadOnlyList<ProgressStep> Steps(IEnumerable<ApplicationHistoryEntry> history, ApplicationStatus current)
    {
        Dictionary<ApplicationStatus, DateTimeOffset> firstReached = history
            .GroupBy(entry => entry.Status)
            .ToDictionary(group => group.Key, group => group.Min(entry => entry.At));

        int furthestReached = ForwardStages
            .Select((status, index) => firstReached.ContainsKey(status) || status == current ? index : -1)
            .Max();

        List<ProgressStep> steps = [];
        for (int index = 0; index < ForwardStages.Length; index++)
        {
            ApplicationStatus status = ForwardStages[index];
            bool reached = firstReached.TryGetValue(status, out DateTimeOffset reachedAt);
            if (reached || index > furthestReached)
            {
                steps.Add(new ProgressStep(status, reached ? reachedAt : null, status == current));
            }
        }

        if (!ForwardStages.Contains(current))
        {
            DateTimeOffset? closedAt = firstReached.TryGetValue(current, out DateTimeOffset at) ? at : null;
            steps.RemoveAll(step => step.ReachedAt is null);
            steps.Add(new ProgressStep(current, closedAt, IsCurrent: true));
        }

        return steps;
    }
}
