using JobHunter.Applications;
using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Jobs;
using JobHunter.Llm;
using JobHunter.Llm.Exchange;
using JobHunter.Pipeline;
using JobHunter.Settings;
using JobHunter.Tests.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Tests.Llm;

/// <summary>Wires the language model and applications modules over a temp SQLite database and a temp exchange folder, with rates that never leave the machine.</summary>
internal sealed class LlmTestHarness : IAsyncDisposable
{
    private readonly string dataFolder = Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider provider;

    public LlmTestHarness()
    {
        ServiceCollection services = new();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddData();
        services.AddSettings();
        services.AddPipeline();
        services.AddLlm();
        services.AddApplications();
        services.AddJobs();
        services.AddSingleton<IFxRateProvider, FakeFxRateProvider>();
        services.AddSingleton(new DataPaths(dataFolder));

        provider = services.BuildServiceProvider();
    }

    public DataPaths Paths => provider.GetRequiredService<DataPaths>();

    public ExchangeExporter Exporter => provider.GetRequiredService<ExchangeExporter>();

    public ExchangeImporter Importer => provider.GetRequiredService<ExchangeImporter>();

    public NewJobImporter NewJobs => provider.GetRequiredService<NewJobImporter>();

    public CoverLetterService CoverLetters => provider.GetRequiredService<CoverLetterService>();

    public JobQueryService JobQueries => provider.GetRequiredService<JobQueryService>();

    /// <summary>Where the settings point the resume markdown, inside the temp folder.</summary>
    public string ResumeMarkdownPath => Path.Combine(dataFolder, "resume.md");

    public SettingsService Settings => provider.GetRequiredService<SettingsService>();

    public IDbContextFactory<JobHunterDbContext> ContextFactory => provider.GetRequiredService<IDbContextFactory<JobHunterDbContext>>();

    /// <summary>Creates the database and points the resume paths inside the temp folder, so that no test reads the real resume.</summary>
    public async Task InitializeAsync()
    {
        await provider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        await Settings.ApplyAsync(settings => settings.ConfigureResume(Path.Combine(dataFolder, "resume.pdf"), ResumeMarkdownPath));
    }

    public string ExchangeFile(string name)
    {
        return Path.Combine(Paths.Exchange, name);
    }

    public async Task WriteExchangeFileAsync(string name, params string[] lines)
    {
        await File.WriteAllTextAsync(ExchangeFile(name), string.Join('\n', lines) + '\n');
    }

    public async Task<string[]> ReadExchangeLinesAsync(string name)
    {
        return await File.ReadAllLinesAsync(ExchangeFile(name));
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

    public async Task<Application> GetApplicationAsync(Guid jobId)
    {
        await using JobHunterDbContext context = await ContextFactory.CreateDbContextAsync();

        return await context.Applications.AsNoTracking().SingleAsync(application => application.JobId == jobId);
    }

    public async ValueTask DisposeAsync()
    {
        await provider.DisposeAsync();
        TestDataFolder.Delete(dataFolder);
    }
}
