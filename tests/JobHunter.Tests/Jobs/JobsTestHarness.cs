using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Jobs;
using JobHunter.Pipeline;
using JobHunter.Settings;
using JobHunter.Sources;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Tests.Jobs;

/// <summary>Wires the job read models and the manual job service over a temp SQLite database, following the temp-DB pattern the data tests use.</summary>
internal sealed class JobsTestHarness : IAsyncDisposable
{
    private readonly string dataFolder = Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider provider;

    public JobsTestHarness()
    {
        ServiceCollection services = new();
        services.AddData();
        services.AddSettings();
        services.AddPipeline();
        services.AddJobs();
        services.AddSingleton(new DataPaths(dataFolder));

        provider = services.BuildServiceProvider();
    }

    public IDbContextFactory<JobHunterDbContext> ContextFactory => provider.GetRequiredService<IDbContextFactory<JobHunterDbContext>>();

    public JobQueryService Queries => provider.GetRequiredService<JobQueryService>();

    public ManualJobService ManualJobs => provider.GetRequiredService<ManualJobService>();

    public JobRetentionService Retention => provider.GetRequiredService<JobRetentionService>();

    public async Task InitializeAsync()
    {
        await provider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
    }

    public async Task SaveAsync(params Job[] jobs)
    {
        await using JobHunterDbContext context = await ContextFactory.CreateDbContextAsync();
        context.Jobs.AddRange(jobs);
        await context.SaveChangesAsync();
    }

    public async Task SaveAsync(Application application)
    {
        await using JobHunterDbContext context = await ContextFactory.CreateDbContextAsync();
        context.Applications.Add(application);
        await context.SaveChangesAsync();
    }

    public async Task<Job> GetJobAsync(Guid jobId)
    {
        await using JobHunterDbContext context = await ContextFactory.CreateDbContextAsync();

        return await context.Jobs.AsNoTracking().SingleAsync(job => job.Id == jobId);
    }

    public async ValueTask DisposeAsync()
    {
        await provider.DisposeAsync();
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(dataFolder))
        {
            Directory.Delete(dataFolder, recursive: true);
        }
    }
}

/// <summary>Builds jobs for the job-list tests in the states the inbox rules care about.</summary>
internal static class ListedJobs
{
    /// <summary>A job in the state the inbox expects: active, passed, new, scored and classed.</summary>
    public static Job NewInboxJob(string company, JobClass jobClass, decimal? compMaxEurYear, DateTimeOffset firstSeenAt, string title = "Backend Engineer", DateTimeOffset? postedAt = null, int scoreTotal = 12)
    {
        Job job = NewJob(company, title, firstSeenAt);
        job.ApplyPrefilterVerdict(PrefilterState.Passed, null, [JobFlag.H1]);

        if (postedAt is DateTimeOffset posted)
        {
            job.RecordPostingFacts(null, null, null, [], null, posted);
        }

        if (compMaxEurYear is decimal comp)
        {
            job.RecordCompensation(comp - 10_000m, comp, "EUR", CompPeriod.Year, comp - 10_000m, comp);
        }

        job.RecordScore(NewScoreCard(firstSeenAt, scoreTotal), jobClass);

        return job;
    }

    /// <summary>A bare job at its first sighting, with one source behind it.</summary>
    public static Job NewJob(string company, string title, DateTimeOffset firstSeenAt, JobSourceKind source = JobSourceKind.RemoteOk, bool isManual = false)
    {
        Job job = Job.Create(
            $"fp-{Guid.NewGuid():N}",
            $"https://jobs.example.com/{Guid.NewGuid():N}",
            $"https://jobs.example.com/{Guid.NewGuid():N}",
            company,
            title,
            "Plain text description.",
            $"hash-{Guid.NewGuid():N}",
            firstSeenAt,
            isManual);

        job.RecordSource(source, $"source-{Guid.NewGuid():N}", firstSeenAt);

        return job;
    }

    /// <summary>A score card whose total matches a class A job.</summary>
    public static ScoreCard NewScoreCard(DateTimeOffset scoredAt, int total = 12)
    {
        return new ScoreCard(2, 2, 2, 1, 2, 2, 1, total, "Strong platform fit.", "senior", "remote", "b2b", null, null, null, null, "Overlaps European hours.", false, true, "Agent tooling.", ["timezone"], "claude-opus-5", scoredAt, "hash-1");
    }
}
