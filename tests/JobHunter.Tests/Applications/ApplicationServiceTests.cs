using JobHunter.Domain;

namespace JobHunter.Tests.Applications;

/// <summary>Covers the application-level changes: mark applied, status transitions, notes, contact, next action and comp discussed.</summary>
public sealed class ApplicationServiceTests : IAsyncLifetime
{
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
    public async Task MarkAppliedAsync_WhenNoApplicationExistsYet_CreatesOneAtApplied()
    {
        Job job = TestJobs.NewJob("Example Co", [], null, isActive: true, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);

        Application application = await harness.Applications.MarkAppliedAsync(job.Id, ApplicationChannel.LinkedIn, "resume-v3.pdf", "applied through LinkedIn");

        Assert.Equal(ApplicationStatus.Applied, application.Status);
        Assert.Equal(ApplicationChannel.LinkedIn, application.Channel);
        Assert.Equal("resume-v3.pdf", application.CvVersion);
        Assert.NotNull(application.AppliedAt);
        Assert.Equal(1, await harness.CountApplicationsAsync());
    }

    [Fact]
    public async Task MarkAppliedAsync_WhenAnApplicationAlreadyExistsAtSaved_TransitionsItAndAppendsHistory()
    {
        Job job = TestJobs.NewScoredJob("Example Co", JobClass.A, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);
        Application pursued = await harness.Triage.PursueAsync(job.Id);

        Application application = await harness.Applications.MarkAppliedAsync(job.Id, ApplicationChannel.Ats, "resume-v3.pdf", "submitted through the form");

        Assert.Equal(pursued.Id, application.Id);
        Assert.Equal(ApplicationStatus.Applied, application.Status);
        Assert.Equal(2, application.History.Count);
        Assert.Equal(1, await harness.CountApplicationsAsync());
    }

    [Fact]
    public async Task ChangeStatusAsync_WhenTheNewStatusSkipsStages_AppliesItAndAppendsAHistoryRowWithTheNote()
    {
        Job job = TestJobs.NewScoredJob("Example Co", JobClass.A, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);
        Application pursued = await harness.Triage.PursueAsync(job.Id);

        Application application = await harness.Applications.ChangeStatusAsync(pursued.Id, ApplicationStatus.SystemDesign, "skipped straight to the system design round");

        Assert.Equal(ApplicationStatus.SystemDesign, application.Status);
        Assert.Equal(2, application.History.Count);
        Assert.Equal("skipped straight to the system design round", application.History[^1].Note);
    }

    [Fact]
    public async Task AddNoteAsync_OnAnApplicationAtSaved_AppendsToTheTimelineAndLeavesTheStatus()
    {
        Job job = TestJobs.NewScoredJob("Example Co", JobClass.A, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);
        Application pursued = await harness.Triage.PursueAsync(job.Id);

        Application application = await harness.Applications.AddNoteAsync(pursued.Id, "Recruiter says two weeks to a reply.");

        Assert.Equal("Recruiter says two weeks to a reply.", Assert.Single(application.Notes).Text);
        Assert.Equal(ApplicationStatus.Saved, application.Status);
    }

    [Fact]
    public async Task SetContactAsync_WithAndThenWithoutAName_RecordsTheContactAndClearsItAgain()
    {
        Job job = TestJobs.NewScoredJob("Example Co", JobClass.A, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);
        Application pursued = await harness.Triage.PursueAsync(job.Id);

        Application withContact = await harness.Applications.SetContactAsync(pursued.Id, "Jane Recruiter", "Talent partner", "jane@example.com");
        Assert.NotNull(withContact.Contact);
        Assert.Equal("Jane Recruiter", withContact.Contact.Name);

        Application withoutContact = await harness.Applications.SetContactAsync(pursued.Id, null, null, null);
        Assert.Null(withoutContact.Contact);
    }

    [Fact]
    public async Task SetNextActionAsync_WithAnActionAndADueDate_RecordsBoth()
    {
        Job job = TestJobs.NewScoredJob("Example Co", JobClass.A, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);
        Application pursued = await harness.Triage.PursueAsync(job.Id);

        Application application = await harness.Applications.SetNextActionAsync(pursued.Id, "Send a follow-up email", new DateOnly(2026, 9, 20));

        Assert.Equal("Send a follow-up email", application.NextAction);
        Assert.Equal(new DateOnly(2026, 9, 20), application.NextActionDue);
    }

    [Fact]
    public async Task SetCompDiscussedAsync_WithFreeText_RecordsItAsGiven()
    {
        Job job = TestJobs.NewScoredJob("Example Co", JobClass.A, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        await harness.SaveAsync(job);
        Application pursued = await harness.Triage.PursueAsync(job.Id);

        Application application = await harness.Applications.SetCompDiscussedAsync(pursued.Id, "Recruiter mentioned a band around the target.");

        Assert.Equal("Recruiter mentioned a band around the target.", application.CompDiscussed);
    }
}
