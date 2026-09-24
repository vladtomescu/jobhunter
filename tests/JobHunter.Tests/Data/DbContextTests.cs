using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Settings;
using JobHunter.Sources;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Tests.Data;

/// <summary>Proves that the real registration path creates the database through the migration, seeds the settings row and round-trips the owned JSON columns.</summary>
public sealed class DbContextTests : IDisposable
{
    private readonly string dataFolder = Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InitializeAsync_OnAnEmptyDataFolder_CreatesTheDatabaseFile()
    {
        await using ServiceProvider provider = BuildProvider();

        await provider.GetRequiredService<DatabaseInitializer>().InitializeAsync(CancellationToken.None);

        Assert.True(File.Exists(provider.GetRequiredService<DataPaths>().DatabaseFile));
    }

    [Fact]
    public async Task GetAsync_AfterInitialization_ReturnsTheSeededSettingsRow()
    {
        await using ServiceProvider provider = BuildProvider();
        await provider.GetRequiredService<DatabaseInitializer>().InitializeAsync(CancellationToken.None);

        JobHunter.Domain.Settings settings = await provider.GetRequiredService<SettingsService>().GetAsync(CancellationToken.None);

        Assert.Equal(JobHunter.Domain.Settings.SingletonId, settings.Id);
        Assert.Equal("claude-opus-5", settings.ScoreModel);
        Assert.Equal("claude-opus-5", settings.KitModel);
        Assert.Equal(300, settings.MaxScoresPerRun);
        Assert.Equal("greenhouse,lever,ashby,workable", settings.DatasetAtsList);
        Assert.Equal(21, settings.FirstRunWindowDays);
        Assert.Equal(21, settings.GhostThresholdDays);
        Assert.Equal(12, settings.AutoRefreshAfterHours);
        Assert.True(settings.RemoteOkEnabled);
        Assert.True(settings.WwrEnabled);
        Assert.True(settings.DatasetEnabled);
    }

    [Fact]
    public async Task GetAsync_OnAFreshDatabase_SeedsTheNeutralCandidateDefaults()
    {
        await using ServiceProvider provider = BuildProvider();
        await provider.GetRequiredService<DatabaseInitializer>().InitializeAsync(CancellationToken.None);

        JobHunter.Domain.Settings settings = await provider.GetRequiredService<SettingsService>().GetAsync(CancellationToken.None);

        Assert.Null(settings.HomeCountryIso);
        Assert.True(settings.AcceptEuropeRemote);
        Assert.True(settings.AcceptUnitedStatesRemote);
        Assert.Equal("en", settings.AcceptedLanguages);
        Assert.Equal("EUR", settings.BaseCurrency);
        Assert.Equal("EUR", settings.CompComputedInCurrency);
        Assert.Equal(string.Empty, settings.StackKeywords);
        Assert.Equal(ContractPreference.Either, settings.ContractPreference);
        Assert.False(settings.HasUnitedStatesWorkAuthorization);
        Assert.Null(settings.HighPayThresholdPerYear);
        Assert.Equal(JobHunter.Domain.Settings.DefaultTitleIncludeTerms, settings.TitleIncludeTerms);
        Assert.Equal(JobHunter.Domain.Settings.DefaultTitleExcludeTerms, settings.TitleExcludeTerms);
        Assert.Contains("\nengineer\n", settings.TitleIncludeTerms);
        Assert.Contains("\nmanager\n", settings.TitleExcludeTerms);
        Assert.Equal("Resume.pdf", Path.GetFileName(settings.ResumePdfPath));
        Assert.Equal("Resume.md", Path.GetFileName(settings.ResumeMarkdownPath));
    }

    [Fact]
    public async Task ApplyAsync_WithConfigureCandidate_PersistsEveryCandidateField()
    {
        await using ServiceProvider provider = BuildProvider();
        await provider.GetRequiredService<DatabaseInitializer>().InitializeAsync(CancellationToken.None);
        SettingsService settingsService = provider.GetRequiredService<SettingsService>();

        await settingsService.ApplyAsync(settings => settings.ConfigureCandidate(" de ", true, false, "EN, De", " usd ", "Java, Kotlin", ContractPreference.Employee, true, 150_000m, "java\r\n\r\n  kotlin  \r\n", "manager\r\n"), CancellationToken.None);
        JobHunter.Domain.Settings stored = await settingsService.GetAsync(CancellationToken.None);

        Assert.Equal("DE", stored.HomeCountryIso);
        Assert.True(stored.AcceptEuropeRemote);
        Assert.False(stored.AcceptUnitedStatesRemote);
        Assert.Equal("en,de", stored.AcceptedLanguages);
        Assert.Equal("USD", stored.BaseCurrency);
        Assert.Equal("EUR", stored.CompComputedInCurrency);
        Assert.Equal("Java, Kotlin", stored.StackKeywords);
        Assert.Equal(ContractPreference.Employee, stored.ContractPreference);
        Assert.True(stored.HasUnitedStatesWorkAuthorization);
        Assert.Equal(150_000m, stored.HighPayThresholdPerYear);
        Assert.Equal("java\nkotlin", stored.TitleIncludeTerms);
        Assert.Equal("manager", stored.TitleExcludeTerms);
    }

    [Fact]
    public async Task SaveChangesAsync_ForAScoredJob_RoundTripsTheOwnedCollectionsAndScoreCard()
    {
        await using ServiceProvider provider = BuildProvider();
        await provider.GetRequiredService<DatabaseInitializer>().InitializeAsync(CancellationToken.None);
        IDbContextFactory<JobHunterDbContext> contextFactory = provider.GetRequiredService<IDbContextFactory<JobHunterDbContext>>();
        DateTimeOffset seenAt = new(2026, 9, 14, 8, 0, 0, TimeSpan.Zero);

        Job job = Job.Create("fingerprint-1", "https://jobs.example.com/backend", "https://jobs.example.com/backend", "Example", "Backend Engineer", "Plain text description.", "hash-1", seenAt, isManual: false);
        job.RecordSource(JobSourceKind.RemoteOk, "remoteok-42", seenAt);
        job.RecordPostingFacts("https://jobs.example.com/backend/apply", null, AtsKind.Greenhouse, ["kotlin", "distributed"], "b2b", seenAt.AddDays(-2));
        job.RecordCompensation(90_000m, 120_000m, "USD", CompPeriod.Year, 82_000m, 110_000m, 110_000m);
        job.ApplyPrefilterVerdict(PrefilterState.Passed, null, [JobFlag.H1, JobFlag.H3], 110_000m);
        job.RecordScore(NewScoreCard(seenAt), JobClass.A);

        await using (JobHunterDbContext writeContext = await contextFactory.CreateDbContextAsync(CancellationToken.None))
        {
            writeContext.Jobs.Add(job);
            await writeContext.SaveChangesAsync(CancellationToken.None);
        }

        await using JobHunterDbContext readContext = await contextFactory.CreateDbContextAsync(CancellationToken.None);
        Job stored = await readContext.Jobs.AsNoTracking().SingleAsync(candidate => candidate.Fingerprint == "fingerprint-1", CancellationToken.None);

        Assert.Equal(JobClass.A, stored.Class);
        Assert.Equal(ScoringState.Scored, stored.Scoring);
        Assert.Equal<string>(["kotlin", "distributed"], stored.Tags);
        Assert.Equal<JobFlag>([JobFlag.H1, JobFlag.H3, JobFlag.HighPay], stored.Flags);
        Assert.Equal(JobSourceKind.RemoteOk, Assert.Single(stored.Sources).Kind);
        Assert.NotNull(stored.Score);
        Assert.Equal(12, stored.Score.Total);
        Assert.Equal<string>(["timezone"], stored.Score.BlockingUnknowns);
        Assert.Equal(110_000m, stored.CompMaxPerYear);
    }

    [Fact]
    public async Task SaveChangesAsync_ForAnApplicationWithAKit_RoundTripsTheNestedAnswers()
    {
        await using ServiceProvider provider = BuildProvider();
        await provider.GetRequiredService<DatabaseInitializer>().InitializeAsync(CancellationToken.None);
        IDbContextFactory<JobHunterDbContext> contextFactory = provider.GetRequiredService<IDbContextFactory<JobHunterDbContext>>();
        DateTimeOffset savedAt = new(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);

        Application application = Application.Create(Guid.CreateVersion7(), ApplicationStatus.Saved, savedAt, "pursued from the inbox");
        application.AttachKit(new ApplicationKit(["fit one", "fit two"], "Cover note.", ["What does the first month look like"], @"G:\resume.pdf", "en", savedAt, "claude-opus-5", [])
        {
            AtsAnswers = [new AtsAnswer("Why this company", "Because of the product.")]
        });
        application.MarkApplied(savedAt.AddHours(1), ApplicationChannel.Ats, "Resume.pdf", "submitted through the form");
        application.AddNote("Waiting for a reply.", savedAt.AddHours(2));

        await using (JobHunterDbContext writeContext = await contextFactory.CreateDbContextAsync(CancellationToken.None))
        {
            writeContext.Applications.Add(application);
            await writeContext.SaveChangesAsync(CancellationToken.None);
        }

        await using JobHunterDbContext readContext = await contextFactory.CreateDbContextAsync(CancellationToken.None);
        Application stored = await readContext.Applications.AsNoTracking().SingleAsync(CancellationToken.None);

        Assert.Equal(ApplicationStatus.Applied, stored.Status);
        Assert.Equal(2, stored.History.Count);
        Assert.Equal("Waiting for a reply.", Assert.Single(stored.Notes).Text);
        Assert.NotNull(stored.Kit);
        Assert.Equal("Why this company", Assert.Single(stored.Kit.AtsAnswers).Question);
        Assert.Equal(2, stored.Kit.FitSummary.Count);
    }

    [Fact]
    public async Task SaveChangesAsync_ForASecondApplicationOnTheSameJob_IsRejectedByTheDatabase()
    {
        await using ServiceProvider provider = BuildProvider();
        await provider.GetRequiredService<DatabaseInitializer>().InitializeAsync(CancellationToken.None);
        IDbContextFactory<JobHunterDbContext> contextFactory = provider.GetRequiredService<IDbContextFactory<JobHunterDbContext>>();
        DateTimeOffset savedAt = new(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);
        Guid jobId = Guid.CreateVersion7();

        await using (JobHunterDbContext writeContext = await contextFactory.CreateDbContextAsync(CancellationToken.None))
        {
            writeContext.Applications.Add(Application.Create(jobId, ApplicationStatus.Saved, savedAt, "pursued from the inbox"));
            await writeContext.SaveChangesAsync(CancellationToken.None);
        }

        await using JobHunterDbContext secondContext = await contextFactory.CreateDbContextAsync(CancellationToken.None);
        secondContext.Applications.Add(Application.Create(jobId, ApplicationStatus.Saved, savedAt, "pursued a second time"));

        DbUpdateException failure = await Assert.ThrowsAsync<DbUpdateException>(() => secondContext.SaveChangesAsync(CancellationToken.None));

        Assert.IsType<SqliteException>(failure.InnerException);

        await using JobHunterDbContext readContext = await contextFactory.CreateDbContextAsync(CancellationToken.None);
        Assert.Equal(1, await readContext.Applications.CountAsync(CancellationToken.None));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(dataFolder))
        {
            Directory.Delete(dataFolder, recursive: true);
        }
    }

    private static ScoreCard NewScoreCard(DateTimeOffset scoredAt)
    {
        return new ScoreCard(2, 2, 2, 1, 2, 2, 1, 12, "Strong platform fit.", "senior", "remote", "b2b", null, null, null, null, "Overlaps European hours.", false, true, "Agent tooling.", ["timezone"], "claude-opus-5", scoredAt, "hash-1");
    }

    private ServiceProvider BuildProvider()
    {
        ServiceCollection services = new();
        services.AddData();
        services.AddSettings();
        services.AddSingleton(new DataPaths(dataFolder));

        return services.BuildServiceProvider();
    }
}
