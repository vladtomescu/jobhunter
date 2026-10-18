using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobHunter.Tests.Jobs;

/// <summary>Proves that the excluded companies reach stored jobs: their jobs are deleted, whatever legal suffix the name carries, while a job with an application, a job added by hand and the jobs of other companies stay.</summary>
public sealed class ExcludedCompanySweepTests
{
    private static readonly DateTimeOffset SeenAt = new(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_ForAnExcludedCompany_DeletesItsJobsButKeepsTheOnesSomethingHoldsOn()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        Job excluded = ListedJobs.NewInboxJob("Initech", JobClass.B, null, SeenAt);
        Job suffixed = ListedJobs.NewJob("Initech GmbH", "Platform Engineer", SeenAt);
        Job applied = ListedJobs.NewInboxJob("Initech", JobClass.B, null, SeenAt, title: "Staff Engineer");
        Job manual = ListedJobs.NewJob("Initech", "Lead Engineer", SeenAt, isManual: true);
        Job other = ListedJobs.NewInboxJob("Initech Labs", JobClass.B, null, SeenAt);
        await harness.SaveAsync(excluded, suffixed, applied, manual, other);
        await harness.SaveAsync(Application.Create(applied.Id, ApplicationStatus.Applied, SeenAt, "applied"));
        await ExcludeAsync(harness, "Initech Inc.");

        int deleted = await NewSweep(harness).RunAsync();

        Assert.Equal(2, deleted);
        Assert.Equal(new[] { applied.Id, manual.Id, other.Id }.Order(), (await JobIdsAsync(harness)).Order());
    }

    [Fact]
    public async Task RunAsync_WithNoCompanyExcluded_DeletesNothing()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(ListedJobs.NewInboxJob("Initech", JobClass.B, null, SeenAt));

        Assert.Equal(0, await NewSweep(harness).RunAsync());
        Assert.Single(await JobIdsAsync(harness));
    }

    private static ExcludedCompanySweep NewSweep(JobsTestHarness harness)
    {
        return new ExcludedCompanySweep(harness.ContextFactory, NullLogger<ExcludedCompanySweep>.Instance);
    }

    private static async Task ExcludeAsync(JobsTestHarness harness, string companies)
    {
        await harness.SettingsService.ApplyAsync(settings => settings.ConfigureExcludedCompanies(companies));
    }

    private static async Task<List<Guid>> JobIdsAsync(JobsTestHarness harness)
    {
        await using JobHunterDbContext context = await harness.ContextFactory.CreateDbContextAsync();

        return await context.Jobs.AsNoTracking().Select(job => job.Id).ToListAsync();
    }
}
