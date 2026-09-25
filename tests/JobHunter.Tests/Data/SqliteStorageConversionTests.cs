using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Tests.Data;

/// <summary>Proves that money and timestamps are stored in columns SQLite can order and range-compare, so those operations happen in SQL and not in memory.</summary>
public sealed class SqliteStorageConversionTests : IDisposable
{
    private readonly string dataFolder = Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider provider;

    /// <summary>Registers the real data and settings modules over a temporary data folder, the same way the application starts.</summary>
    public SqliteStorageConversionTests()
    {
        ServiceCollection services = new();
        services.AddData();
        services.AddSettings();
        services.AddSingleton(new DataPaths(dataFolder));

        provider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task OrderByDescending_OnNormalizedCompensation_OrdersInSqlWithTheUnknownLast()
    {
        IDbContextFactory<JobHunterDbContext> contextFactory = await MigratedContextFactoryAsync();
        await InsertAsync(contextFactory,
            JobWithCompensation("comp-90", 90_000m),
            JobWithCompensation("comp-none", null),
            JobWithCompensation("comp-100", 100_000m));

        await using JobHunterDbContext readContext = await contextFactory.CreateDbContextAsync(CancellationToken.None);
        IQueryable<decimal?> query = readContext.Jobs.AsNoTracking()
            .OrderByDescending(job => job.CompMaxPerYear)
            .Select(job => job.CompMaxPerYear);
        List<decimal?> ordered = await query.ToListAsync(CancellationToken.None);

        Assert.Contains("ORDER BY", query.ToQueryString(), StringComparison.Ordinal);
        Assert.Equal<decimal?>([100_000m, 90_000m, null], ordered);
    }

    [Fact]
    public async Task WhereAndOrderBy_OnTimestampsWrittenWithDifferentOffsets_FiltersAndOrdersChronologicallyInSql()
    {
        DateTimeOffset nineUtc = new(2026, 9, 14, 12, 0, 0, TimeSpan.FromHours(3));
        DateTimeOffset tenUtc = new(2026, 9, 14, 5, 0, 0, TimeSpan.FromHours(-5));
        DateTimeOffset elevenUtc = new(2026, 9, 14, 14, 0, 0, TimeSpan.FromHours(3));
        DateTimeOffset cutoff = new(2026, 9, 14, 9, 30, 0, TimeSpan.Zero);

        IDbContextFactory<JobHunterDbContext> contextFactory = await MigratedContextFactoryAsync();
        await InsertAsync(contextFactory,
            JobSeenAt("seen-eleven", elevenUtc),
            JobSeenAt("seen-nine", nineUtc),
            JobSeenAt("seen-ten", tenUtc));

        await using JobHunterDbContext readContext = await contextFactory.CreateDbContextAsync(CancellationToken.None);
        IQueryable<DateTimeOffset> query = readContext.Jobs.AsNoTracking()
            .Where(job => job.FirstSeenAt >= cutoff)
            .OrderBy(job => job.FirstSeenAt)
            .Select(job => job.FirstSeenAt);
        List<DateTimeOffset> seenAt = await query.ToListAsync(CancellationToken.None);

        string sql = query.ToQueryString();
        Assert.Contains("WHERE", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY", sql, StringComparison.Ordinal);
        Assert.Equal<DateTimeOffset>([tenUtc, elevenUtc], seenAt);
        Assert.All(seenAt, stored => Assert.Equal(TimeSpan.Zero, stored.Offset));
    }

    public void Dispose()
    {
        provider.Dispose();
        TestDataFolder.Delete(dataFolder);
    }

    private static Job JobWithCompensation(string fingerprint, decimal? maxEurYear)
    {
        Job job = JobSeenAt(fingerprint, new DateTimeOffset(2026, 9, 14, 8, 0, 0, TimeSpan.Zero));
        job.RecordCompensation(null, maxEurYear, maxEurYear is null ? null : "EUR", maxEurYear is null ? null : CompPeriod.Year, null, maxEurYear);

        return job;
    }

    private static Job JobSeenAt(string fingerprint, DateTimeOffset seenAt)
    {
        return Job.Create(fingerprint, $"https://jobs.example.com/{fingerprint}", $"https://jobs.example.com/{fingerprint}", "Example", "Backend Engineer", "Plain text description.", $"hash-{fingerprint}", seenAt, isManual: false);
    }

    private static async Task InsertAsync(IDbContextFactory<JobHunterDbContext> contextFactory, params Job[] jobs)
    {
        await using JobHunterDbContext writeContext = await contextFactory.CreateDbContextAsync(CancellationToken.None);
        writeContext.Jobs.AddRange(jobs);
        await writeContext.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<IDbContextFactory<JobHunterDbContext>> MigratedContextFactoryAsync()
    {
        await provider.GetRequiredService<DatabaseInitializer>().InitializeAsync(CancellationToken.None);

        return provider.GetRequiredService<IDbContextFactory<JobHunterDbContext>>();
    }
}
