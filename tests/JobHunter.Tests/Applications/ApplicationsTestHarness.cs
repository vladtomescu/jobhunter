using JobHunter.Applications;
using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Llm;
using JobHunter.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Tests.Applications;

/// <summary>Wires the applications module over a temp SQLite database with a fake kit writer, mirroring the temp-DB pattern in DbContextTests.</summary>
internal sealed class ApplicationsTestHarness : IAsyncDisposable
{
    private readonly string dataFolder = Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider provider;

    public ApplicationsTestHarness()
    {
        ServiceCollection services = new();
        services.AddData();
        services.AddSettings();
        services.AddSingleton<IKitWriter>(KitWriter);
        services.AddApplications();
        services.AddSingleton(new DataPaths(dataFolder));

        provider = services.BuildServiceProvider();
    }

    public FakeKitWriter KitWriter { get; } = new();

    public IDbContextFactory<JobHunterDbContext> ContextFactory => provider.GetRequiredService<IDbContextFactory<JobHunterDbContext>>();

    public TriageService Triage => provider.GetRequiredService<TriageService>();

    public ApplicationService Applications => provider.GetRequiredService<ApplicationService>();

    public GhostCandidateQuery GhostCandidates => provider.GetRequiredService<GhostCandidateQuery>();

    public StatsService Stats => provider.GetRequiredService<StatsService>();

    public SettingsService Settings => provider.GetRequiredService<SettingsService>();

    public async Task InitializeAsync()
    {
        await provider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
    }

    public async Task SaveAsync(Job job)
    {
        await using JobHunterDbContext context = await ContextFactory.CreateDbContextAsync();
        context.Jobs.Add(job);
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

    public async Task<Application> GetApplicationAsync(Guid applicationId)
    {
        await using JobHunterDbContext context = await ContextFactory.CreateDbContextAsync();

        return await context.Applications.AsNoTracking().SingleAsync(application => application.Id == applicationId);
    }

    public async Task<Application?> FindApplicationByJobAsync(Guid jobId)
    {
        await using JobHunterDbContext context = await ContextFactory.CreateDbContextAsync();

        return await context.Applications.AsNoTracking().FirstOrDefaultAsync(application => application.JobId == jobId);
    }

    public async Task<int> CountApplicationsAsync()
    {
        await using JobHunterDbContext context = await ContextFactory.CreateDbContextAsync();

        return await context.Applications.CountAsync();
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
