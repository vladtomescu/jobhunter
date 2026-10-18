using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Jobs;
using JobHunter.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobHunter.Tests.Jobs;

/// <summary>Proves that the startup fold merges the copies of one posting stored as separate jobs into the one that matters most, keeps different openings and jobs added by hand apart, and changes nothing on a second run.</summary>
public sealed class CopyFolderTests
{
    private static readonly DateTimeOffset SeenAt = new(2026, 10, 14, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_ForOnePostingStoredOncePerCountry_KeepsTheFirstSeenWithEverySighting()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        Job first = Copy("Germany", "DE", SeenAt);
        Job second = Copy("Poland", "PL", SeenAt.AddMinutes(1));
        Job third = Copy("Spain", "ES", SeenAt.AddMinutes(2));
        await harness.SaveAsync(first, second, third);

        int folded = await NewFolder(harness).RunAsync();

        Assert.Equal(2, folded);
        Job kept = Assert.Single(await JobsAsync(harness));
        Assert.Equal(first.Id, kept.Id);
        Assert.Equal(3, kept.Sources.Count);
    }

    [Fact]
    public async Task RunAsync_WhenALaterCopyHasAnApplication_KeepsThatCopy()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        Job first = Copy("Germany", "DE", SeenAt);
        Job applied = Copy("Poland", "PL", SeenAt.AddMinutes(1));
        await harness.SaveAsync(first, applied);
        await harness.SaveAsync(Application.Create(applied.Id, ApplicationStatus.Applied, SeenAt, "applied"));

        await NewFolder(harness).RunAsync();

        Assert.Equal(applied.Id, Assert.Single(await JobsAsync(harness)).Id);
    }

    [Fact]
    public async Task RunAsync_ForTheSameTitleWithOtherText_KeepsBothOpenings()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(Copy("Germany", "DE", SeenAt), Copy("Poland", "PL", SeenAt, descriptionHash: "other-text"));

        int folded = await NewFolder(harness).RunAsync();

        Assert.Equal(0, folded);
        Assert.Equal(2, (await JobsAsync(harness)).Count);
    }

    [Fact]
    public async Task RunAsync_ASecondTime_FoldsNothing()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(Copy("Germany", "DE", SeenAt), Copy("Poland", "PL", SeenAt.AddMinutes(1)));
        CopyFolder folder = NewFolder(harness);
        await folder.RunAsync();

        Assert.Equal(0, await folder.RunAsync());
    }

    private static Job Copy(string location, string countryIso, DateTimeOffset seenAt, string descriptionHash = "same-text")
    {
        string id = Guid.NewGuid().ToString("N");
        Job job = Job.Create($"fp-{id}", $"https://jobs.example.com/{id}", $"https://jobs.example.com/{id}", "Northwind", "Technical Lead", "Plain text description.", descriptionHash, seenAt, isManual: false);
        job.RecordSource(JobSourceKind.Dataset, $"ashby:{id}", seenAt);
        job.RecordPlace(location, countryIso, null, true, "en");

        return job;
    }

    private static CopyFolder NewFolder(JobsTestHarness harness)
    {
        return new CopyFolder(harness.ContextFactory, NullLogger<CopyFolder>.Instance);
    }

    private static async Task<List<Job>> JobsAsync(JobsTestHarness harness)
    {
        await using JobHunterDbContext context = await harness.ContextFactory.CreateDbContextAsync();

        return await context.Jobs.AsNoTracking().ToListAsync();
    }
}
