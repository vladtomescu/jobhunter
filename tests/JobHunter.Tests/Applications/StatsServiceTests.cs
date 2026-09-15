using JobHunter.Domain;
using JobHunter.Sources;

namespace JobHunter.Tests.Applications;

/// <summary>Seeds a small, fully hand-counted set of jobs and applications and checks every statistic against the hand count.</summary>
public sealed class StatsServiceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset AsOf = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly ApplicationsTestHarness harness = new();

    public Task InitializeAsync()
    {
        return harness.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        return harness.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task GetSnapshotAsync_OnASeededSet_MatchesTheHandCount()
    {
        await SeedAsync();

        JobHunter.Applications.StatsSnapshot snapshot = await harness.Stats.GetSnapshotAsync(AsOf);

        Assert.Equal(1, snapshot.StatusCounts[ApplicationStatus.Saved]);
        Assert.Equal(3, snapshot.StatusCounts[ApplicationStatus.Applied]);
        Assert.Equal(2, snapshot.StatusCounts[ApplicationStatus.Screening]);
        Assert.Equal(1, snapshot.StatusCounts[ApplicationStatus.Interview1]);
        Assert.Equal(0, snapshot.StatusCounts[ApplicationStatus.Interview2]);
        Assert.Equal(0, snapshot.StatusCounts[ApplicationStatus.Offer]);
        Assert.Equal(2, snapshot.StatusCounts[ApplicationStatus.Rejected]);
        Assert.Equal(9, snapshot.StatusCounts.Values.Sum());

        Assert.Equal(12, snapshot.ApplicationsPerWeek.Count);
        Assert.Equal(new DateOnly(2026, 9, 14), snapshot.ApplicationsPerWeek[^1].WeekStart);
        Assert.Equal(2, CountForWeekStarting(snapshot, new DateOnly(2026, 8, 10)));
        Assert.Equal(1, CountForWeekStarting(snapshot, new DateOnly(2026, 8, 31)));
        Assert.Equal(3, CountForWeekStarting(snapshot, new DateOnly(2026, 9, 7)));
        Assert.Equal(0, CountForWeekStarting(snapshot, new DateOnly(2026, 9, 14)));
        Assert.Equal(6, snapshot.ApplicationsPerWeek.Sum(week => week.Count));

        Assert.Equal(4.0 / 8.0, snapshot.ResponseRate);
        Assert.Equal(2.0, snapshot.MedianDaysToFirstReply);

        Assert.Equal(3, snapshot.BySource[JobSourceKind.RemoteOk]);
        Assert.Equal(2, snapshot.BySource[JobSourceKind.WeWorkRemotely]);
        Assert.Equal(2, snapshot.BySource[JobSourceKind.Dataset]);
        Assert.False(snapshot.BySource.ContainsKey(JobSourceKind.Manual));

        Assert.Equal(2, snapshot.ByAts[AtsKind.Greenhouse]);
        Assert.Equal(2, snapshot.ByAts[AtsKind.Lever]);
        Assert.Equal(1, snapshot.ByAts[AtsKind.Ashby]);
        Assert.Equal(1, snapshot.ByAts[AtsKind.Workable]);

        Assert.Equal(1, snapshot.PursuedNotAppliedCount);
        Assert.Equal(1, snapshot.FollowUpsDueCount);
        Assert.Equal(1, snapshot.FollowUpsOverdueCount);
        Assert.Equal(1, snapshot.InactiveWithOpenApplicationCount);
    }

    private static int CountForWeekStarting(JobHunter.Applications.StatsSnapshot snapshot, DateOnly weekStart)
    {
        return snapshot.ApplicationsPerWeek.Single(week => week.WeekStart == weekStart).Count;
    }

    private async Task SeedAsync()
    {
        await SeedAppliedThenRepliedAsync(
            "Job One", [JobSourceKind.RemoteOk], AtsKind.Greenhouse, isActive: true,
            appliedAt: new DateTimeOffset(2026, 8, 10, 8, 0, 0, TimeSpan.Zero),
            replyStatus: ApplicationStatus.Screening,
            replyAt: new DateTimeOffset(2026, 8, 14, 8, 0, 0, TimeSpan.Zero),
            finalStatus: ApplicationStatus.Rejected,
            finalAt: new DateTimeOffset(2026, 8, 20, 8, 0, 0, TimeSpan.Zero));

        await SeedAppliedThenRepliedAsync(
            "Job Two", [JobSourceKind.WeWorkRemotely], AtsKind.Lever, isActive: true,
            appliedAt: new DateTimeOffset(2026, 8, 12, 9, 0, 0, TimeSpan.Zero),
            replyStatus: ApplicationStatus.Interview1,
            replyAt: new DateTimeOffset(2026, 8, 13, 9, 0, 0, TimeSpan.Zero),
            finalStatus: null,
            finalAt: null);

        await SeedAppliedOnlyAsync(
            "Job Three", [JobSourceKind.Dataset], AtsKind.Ashby, isActive: false,
            appliedAt: new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero));

        await SeedAppliedThenRepliedAsync(
            "Job Four", [JobSourceKind.RemoteOk], AtsKind.Greenhouse, isActive: true,
            appliedAt: new DateTimeOffset(2026, 9, 7, 7, 0, 0, TimeSpan.Zero),
            replyStatus: ApplicationStatus.Screening,
            replyAt: new DateTimeOffset(2026, 9, 10, 7, 0, 0, TimeSpan.Zero),
            finalStatus: null,
            finalAt: null);

        await SeedPursuedNotAppliedAsync("Job Five", [], null, isActive: true, savedAt: new DateTimeOffset(2026, 9, 5, 8, 0, 0, TimeSpan.Zero));

        await SeedAppliedThenRepliedAsync(
            "Job Six", [JobSourceKind.RemoteOk], null, isActive: true,
            appliedAt: new DateTimeOffset(2026, 6, 15, 8, 0, 0, TimeSpan.Zero),
            replyStatus: ApplicationStatus.Rejected,
            replyAt: new DateTimeOffset(2026, 6, 16, 8, 0, 0, TimeSpan.Zero),
            finalStatus: null,
            finalAt: null);

        await SeedAppliedOnlyAsync(
            "Job Seven", [JobSourceKind.Dataset], AtsKind.Workable, isActive: true,
            appliedAt: new DateTimeOffset(2026, 9, 10, 8, 0, 0, TimeSpan.Zero),
            nextAction: "Send a follow-up email",
            nextActionDue: new DateOnly(2026, 9, 10));

        await SeedAppliedOnlyAsync(
            "Job Eight", [JobSourceKind.WeWorkRemotely], AtsKind.Lever, isActive: true,
            appliedAt: new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero),
            nextAction: "Call the recruiter",
            nextActionDue: new DateOnly(2026, 9, 14));

        await SeedRepliedWithoutAnAppliedRowAsync("Job Nine", savedAt: new DateTimeOffset(2026, 9, 8, 8, 0, 0, TimeSpan.Zero));

        await harness.SaveAsync(TestJobs.NewJob("Job Ten", [JobSourceKind.RemoteOk], AtsKind.Greenhouse, isActive: false, new DateTimeOffset(2026, 9, 2, 8, 0, 0, TimeSpan.Zero)));
    }

    private async Task SeedAppliedThenRepliedAsync(
        string company, IEnumerable<JobSourceKind> sources, AtsKind? ats, bool isActive,
        DateTimeOffset appliedAt, ApplicationStatus replyStatus, DateTimeOffset replyAt,
        ApplicationStatus? finalStatus, DateTimeOffset? finalAt)
    {
        Job job = TestJobs.NewJob(company, sources, ats, isActive, appliedAt);
        await harness.SaveAsync(job);

        Application application = Application.Create(job.Id, ApplicationStatus.Saved, appliedAt.AddHours(-1), "pursued");
        application.MoveTo(ApplicationStatus.Applied, appliedAt, null);
        application.MoveTo(replyStatus, replyAt, null);
        if (finalStatus is ApplicationStatus status && finalAt is DateTimeOffset at)
        {
            application.MoveTo(status, at, null);
        }

        await harness.SaveAsync(application);
    }

    private async Task SeedAppliedOnlyAsync(string company, IEnumerable<JobSourceKind> sources, AtsKind? ats, bool isActive, DateTimeOffset appliedAt, string? nextAction = null, DateOnly? nextActionDue = null)
    {
        Job job = TestJobs.NewJob(company, sources, ats, isActive, appliedAt);
        await harness.SaveAsync(job);

        Application application = Application.Create(job.Id, ApplicationStatus.Saved, appliedAt.AddHours(-1), "pursued");
        application.MoveTo(ApplicationStatus.Applied, appliedAt, null);
        application.PlanNextAction(nextAction, nextActionDue);

        await harness.SaveAsync(application);
    }

    private async Task SeedRepliedWithoutAnAppliedRowAsync(string company, DateTimeOffset savedAt)
    {
        Job job = TestJobs.NewJob(company, [], null, isActive: true, savedAt);
        await harness.SaveAsync(job);

        Application application = Application.Create(job.Id, ApplicationStatus.Saved, savedAt, "pursued");
        application.MoveTo(ApplicationStatus.Screening, savedAt.AddDays(2), "recruiter reached out before the form was ever submitted");

        await harness.SaveAsync(application);
    }

    private async Task SeedPursuedNotAppliedAsync(string company, IEnumerable<JobSourceKind> sources, AtsKind? ats, bool isActive, DateTimeOffset savedAt)
    {
        Job job = TestJobs.NewJob(company, sources, ats, isActive, savedAt);
        await harness.SaveAsync(job);

        Application application = Application.Create(job.Id, ApplicationStatus.Saved, savedAt, "pursued");
        await harness.SaveAsync(application);
    }
}
