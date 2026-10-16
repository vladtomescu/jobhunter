using JobHunter.Applications;
using JobHunter.Domain;

namespace JobHunter.Tests.Applications;

/// <summary>Covers the job page's stepper: reached stages with their first date, skipped stages left out, the stages ahead, and the closing status.</summary>
public sealed class ApplicationProgressTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Steps_ForAnApplicationThatSkippedManager_ListsTheReachedStagesThenTheStagesAheadWithoutManager()
    {
        ApplicationHistoryEntry[] history =
        [
            new(ApplicationStatus.Saved, Start, null),
            new(ApplicationStatus.Applied, Start.AddDays(1), null),
            new(ApplicationStatus.Screening, Start.AddDays(5), null),
            new(ApplicationStatus.Tech, Start.AddDays(8), null)
        ];

        IReadOnlyList<ProgressStep> steps = ApplicationProgress.Steps(history, ApplicationStatus.Tech);

        Assert.Equal<ApplicationStatus>(
            [ApplicationStatus.Saved, ApplicationStatus.Applied, ApplicationStatus.Screening, ApplicationStatus.Tech, ApplicationStatus.SystemDesign, ApplicationStatus.Fit, ApplicationStatus.Offer, ApplicationStatus.Accepted],
            steps.Select(step => step.Status));
        Assert.Equal(Start.AddDays(8), steps[3].ReachedAt);
        Assert.True(steps[3].IsCurrent);
        Assert.Null(steps[4].ReachedAt);
    }

    [Fact]
    public void Steps_ForAStageReachedTwice_DatesItByTheFirstTime()
    {
        ApplicationHistoryEntry[] history =
        [
            new(ApplicationStatus.Saved, Start, null),
            new(ApplicationStatus.Applied, Start.AddDays(1), null),
            new(ApplicationStatus.Saved, Start.AddDays(2), null),
            new(ApplicationStatus.Applied, Start.AddDays(3), null)
        ];

        IReadOnlyList<ProgressStep> steps = ApplicationProgress.Steps(history, ApplicationStatus.Applied);

        Assert.Equal(Start.AddDays(1), steps.Single(step => step.Status == ApplicationStatus.Applied).ReachedAt);
    }

    [Fact]
    public void Steps_ForARejectedApplication_DropsTheStagesAheadAndEndsWithTheRejection()
    {
        ApplicationHistoryEntry[] history =
        [
            new(ApplicationStatus.Saved, Start, null),
            new(ApplicationStatus.Applied, Start.AddDays(1), null),
            new(ApplicationStatus.Rejected, Start.AddDays(9), null)
        ];

        IReadOnlyList<ProgressStep> steps = ApplicationProgress.Steps(history, ApplicationStatus.Rejected);

        Assert.Equal<ApplicationStatus>([ApplicationStatus.Saved, ApplicationStatus.Applied, ApplicationStatus.Rejected], steps.Select(step => step.Status));
        Assert.Equal(Start.AddDays(9), steps[^1].ReachedAt);
        Assert.True(steps[^1].IsCurrent);
    }
}
