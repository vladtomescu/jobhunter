using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobHunter.Tests.Jobs;

/// <summary>Proves that the startup backfill gives stored jobs the high-pay flag they miss, clears a stale one, leaves every other flag alone and changes nothing on a second run.</summary>
public sealed class HighlightFlagBackfillTests
{
    private static readonly DateTimeOffset SeenAt = new(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_ForJobsStoredBeforeTheHighPayFlag_RaisesItAndKeepsTheOtherFlags()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        Job plainJob = ListedJobs.NewJob("Acme", "Senior Backend Engineer", SeenAt);
        Job wellPaidJob = ListedJobs.NewJob("Globex", "Staff Platform Engineer", SeenAt);
        wellPaidJob.RecordCompensation(null, 120_000m, "EUR", CompPeriod.Year, null, 120_000m);
        await harness.SaveAsync(plainJob, wellPaidJob);
        await OverwriteFlagsAsync(harness, plainJob.Id, """["H1","CU"]""");
        await OverwriteFlagsAsync(harness, wellPaidJob.Id, """["H1","WA"]""");

        int changed = await NewBackfill(harness).RunAsync();

        Assert.Equal(1, changed);
        Assert.Equal<JobFlag>([JobFlag.H1, JobFlag.CU], (await harness.GetJobAsync(plainJob.Id)).Flags);
        Assert.Equal<JobFlag>([JobFlag.H1, JobFlag.WA, JobFlag.HighPay], (await harness.GetJobAsync(wellPaidJob.Id)).Flags);
    }

    [Fact]
    public async Task RunAsync_ForAJobWhoseStoredHighPayNoLongerHolds_ClearsIt()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = ListedJobs.NewJob("Initech", "Backend Engineer", SeenAt);
        await harness.SaveAsync(job);
        await OverwriteFlagsAsync(harness, job.Id, """["H3","HighPay"]""");

        int changed = await NewBackfill(harness).RunAsync();

        Assert.Equal(1, changed);
        Assert.Equal<JobFlag>([JobFlag.H3], (await harness.GetJobAsync(job.Id)).Flags);
    }

    [Fact]
    public async Task RunAsync_ASecondTime_ChangesNothing()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = ListedJobs.NewJob("Acme", "Senior Backend Engineer", SeenAt);
        job.RecordCompensation(null, 120_000m, "EUR", CompPeriod.Year, null, 120_000m);
        await harness.SaveAsync(job);
        await OverwriteFlagsAsync(harness, job.Id, """["H1"]""");
        HighlightFlagBackfill backfill = NewBackfill(harness);
        await backfill.RunAsync();

        int changed = await backfill.RunAsync();

        Assert.Equal(0, changed);
    }

    private static HighlightFlagBackfill NewBackfill(JobsTestHarness harness)
    {
        return new HighlightFlagBackfill(harness.ContextFactory, NullLogger<HighlightFlagBackfill>.Instance);
    }

    /// <summary>Writes the flags column directly, the way a job stored before the high-pay flag existed looks.</summary>
    private static async Task OverwriteFlagsAsync(JobsTestHarness harness, Guid jobId, string flagsJson)
    {
        await using JobHunterDbContext context = await harness.ContextFactory.CreateDbContextAsync();
        await context.Database.ExecuteSqlAsync($"UPDATE Jobs SET Flags = {flagsJson} WHERE Id = {jobId}");
    }
}
