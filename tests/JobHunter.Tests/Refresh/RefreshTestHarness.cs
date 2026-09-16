using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Llm;
using JobHunter.Pipeline;
using JobHunter.Refresh;
using JobHunter.Settings;
using JobHunter.Sources;
using JobHunter.Tests.Pipeline;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Tests.Refresh;

/// <summary>A detector that reports a key the test decides on, without touching configuration files or the environment.</summary>
internal sealed class StubApiKeyDetector(bool present) : ApiKeyDetector(new ConfigurationBuilder().Build())
{
    /// <inheritdoc />
    public override string? Read()
    {
        return present ? "test-key" : null;
    }
}

/// <summary>Wires the refresh module over a temp SQLite database with fake sources, a fake scorer and fixed exchange rates, mirroring the temp-DB pattern in DbContextTests.</summary>
internal sealed class RefreshTestHarness : IAsyncDisposable
{
    private readonly string dataFolder = Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider provider;

    public RefreshTestHarness(params IJobSource[] sources)
        : this(apiKeyPresent: true, useLiveSources: false, sources)
    {
    }

    public RefreshTestHarness(bool apiKeyPresent, params IJobSource[] sources)
        : this(apiKeyPresent, useLiveSources: false, sources)
    {
    }

    private RefreshTestHarness(bool apiKeyPresent, bool useLiveSources, IJobSource[] sources)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddData();
        services.AddSettings();
        services.AddPipeline();
        services.AddRefresh();
        services.AddSingleton<IFxRateProvider, FakeFxRateProvider>();
        services.AddSingleton<IJobScorer>(Scorer);
        services.AddSingleton<ApiKeyDetector>(new StubApiKeyDetector(apiKeyPresent));
        services.AddSingleton(new DataPaths(dataFolder));

        if (useLiveSources)
        {
            services.AddSources();
        }

        foreach (IJobSource source in sources)
        {
            services.AddSingleton(source);
        }

        provider = services.BuildServiceProvider();
    }

    /// <summary>A harness wired to the real sources, for the test that reaches the live feeds.</summary>
    public static RefreshTestHarness WithLiveSources()
    {
        return new RefreshTestHarness(apiKeyPresent: true, useLiveSources: true, []);
    }

    /// <summary>The scorer every run in this harness sends its jobs to.</summary>
    public FakeJobScorer Scorer { get; } = new();

    /// <summary>The refresh under test.</summary>
    public RefreshService Refresher => provider.GetRequiredService<RefreshService>();

    /// <summary>The live state the panel reads.</summary>
    public RefreshState State => provider.GetRequiredService<RefreshState>();

    /// <summary>The settings the runs read.</summary>
    public SettingsService Settings => provider.GetRequiredService<SettingsService>();

    /// <summary>The data folder the raw responses are cached under.</summary>
    public string DataFolder => dataFolder;

    public IDbContextFactory<JobHunterDbContext> ContextFactory => provider.GetRequiredService<IDbContextFactory<JobHunterDbContext>>();

    /// <summary>Creates the database through the migration and seeds the settings row.</summary>
    public async Task InitializeAsync()
    {
        await provider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
    }

    /// <summary>Runs one refresh and returns what it produced.</summary>
    public Task<RefreshResult> RunAsync(FetchTrigger trigger = FetchTrigger.Manual)
    {
        return Refresher.RunAsync(trigger, CancellationToken.None);
    }

    public async Task SaveAsync(Job job)
    {
        await using JobHunterDbContext context = await ContextFactory.CreateDbContextAsync();
        context.Jobs.Add(job);
        await context.SaveChangesAsync();
    }

    public async Task SaveAsync(FetchRun run)
    {
        await using JobHunterDbContext context = await ContextFactory.CreateDbContextAsync();
        context.FetchRuns.Add(run);
        await context.SaveChangesAsync();
    }

    public async Task<List<Job>> JobsAsync()
    {
        await using JobHunterDbContext context = await ContextFactory.CreateDbContextAsync();

        return await context.Jobs.AsNoTracking().OrderBy(job => job.FirstSeenAt).ToListAsync();
    }

    public async Task<Job> SingleJobAsync()
    {
        await using JobHunterDbContext context = await ContextFactory.CreateDbContextAsync();

        return await context.Jobs.AsNoTracking().SingleAsync();
    }

    public async Task<Job> JobByPostingUrlAsync(string postingUrl)
    {
        await using JobHunterDbContext context = await ContextFactory.CreateDbContextAsync();

        return await context.Jobs.AsNoTracking().SingleAsync(job => job.PostingUrl == postingUrl);
    }

    public async Task<List<FetchRun>> RunsAsync()
    {
        await using JobHunterDbContext context = await ContextFactory.CreateDbContextAsync();

        return await context.FetchRuns.AsNoTracking().OrderBy(run => run.StartedAt).ToListAsync();
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
