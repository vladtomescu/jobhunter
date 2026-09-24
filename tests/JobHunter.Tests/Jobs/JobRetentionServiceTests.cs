using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Jobs;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Tests.Jobs;

/// <summary>Proves which jobs a delete of old jobs removes: aged by posted date or first sighting, never one with an application or one added by hand, and never below the first-run window.</summary>
public sealed class JobRetentionServiceTests
{
    [Fact]
    public async Task CountJobsOlderThanAsync_BelowTheFirstRunWindow_RefusesNamingTheMinimum()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();

        JobRetentionOutcome outcome = await harness.Retention.CountJobsOlderThanAsync(20);

        Assert.True(outcome.IsRefused);
        Assert.Contains("21 days", outcome.Refusal);
    }

    [Fact]
    public async Task DeleteJobsOlderThanAsync_BelowTheFirstRunWindow_RefusesAndDeletesNothing()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(Dated("Oldco", DateTimeOffset.UtcNow.AddDays(-40), DateTimeOffset.UtcNow.AddDays(-40)));

        JobRetentionOutcome outcome = await harness.Retention.DeleteJobsOlderThanAsync(20);

        Assert.True(outcome.IsRefused);
        Assert.Equal(0, outcome.Jobs);
        Assert.Equal(1, await CountJobsAsync(harness));
    }

    [Fact]
    public async Task DeleteJobsOlderThanAsync_AtTheFirstRunWindow_IsAccepted()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();

        JobRetentionOutcome outcome = await harness.Retention.DeleteJobsOlderThanAsync(21);

        Assert.False(outcome.IsRefused);
    }

    [Fact]
    public async Task DeleteJobsOlderThanAsync_DeletesTheOldJobsAndKeepsRecentApplicationAndManualOnes()
    {
        DateTimeOffset old = DateTimeOffset.UtcNow.AddDays(-40);
        DateTimeOffset recent = DateTimeOffset.UtcNow.AddDays(-5);
        Job postedLongAgo = Dated("Postedold", recent, old);
        Job undatedSeenLongAgo = ListedJobs.NewJob("Seenold", "Backend Engineer", old);
        Job postedRecentlySeenLongAgo = Dated("Postedrecent", old, recent);
        Job undatedSeenRecently = ListedJobs.NewJob("Seenrecent", "Backend Engineer", recent);
        Job oldWithApplication = Dated("Applied", old, old);
        Job oldManual = ListedJobs.NewJob("Handco", "Backend Engineer", old, isManual: true);
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(postedLongAgo, undatedSeenLongAgo, postedRecentlySeenLongAgo, undatedSeenRecently, oldWithApplication, oldManual);
        await harness.SaveAsync(Application.Create(oldWithApplication.Id, ApplicationStatus.Applied, old, "applied long ago"));

        JobRetentionOutcome preview = await harness.Retention.CountJobsOlderThanAsync(30);
        JobRetentionOutcome deleted = await harness.Retention.DeleteJobsOlderThanAsync(30);

        List<Guid> remaining = await RemainingIdsAsync(harness);
        Assert.Equal(2, preview.Jobs);
        Assert.Equal(2, deleted.Jobs);
        Assert.DoesNotContain(postedLongAgo.Id, remaining);
        Assert.DoesNotContain(undatedSeenLongAgo.Id, remaining);
        Assert.Contains(postedRecentlySeenLongAgo.Id, remaining);
        Assert.Contains(undatedSeenRecently.Id, remaining);
        Assert.Contains(oldWithApplication.Id, remaining);
        Assert.Contains(oldManual.Id, remaining);
        Assert.Equal(0, (await harness.Retention.CountJobsOlderThanAsync(30)).Jobs);
    }

    /// <summary>A board job first seen on one day and posted on another.</summary>
    private static Job Dated(string company, DateTimeOffset firstSeenAt, DateTimeOffset postedAt)
    {
        Job job = ListedJobs.NewJob(company, "Backend Engineer", firstSeenAt);
        job.RecordPostingFacts(null, null, null, [], null, postedAt);

        return job;
    }

    private static async Task<int> CountJobsAsync(JobsTestHarness harness)
    {
        await using JobHunterDbContext context = await harness.ContextFactory.CreateDbContextAsync();

        return await context.Jobs.CountAsync();
    }

    private static async Task<List<Guid>> RemainingIdsAsync(JobsTestHarness harness)
    {
        await using JobHunterDbContext context = await harness.ContextFactory.CreateDbContextAsync();

        return await context.Jobs.Select(job => job.Id).ToListAsync();
    }
}
