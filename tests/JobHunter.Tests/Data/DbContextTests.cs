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
        Assert.Equal(string.Empty, settings.HomeCity);
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

        await settingsService.ApplyAsync(settings => settings.ConfigureCandidate(" de ", " Berlin ", true, false, "EN, De", " usd ", "Java, Kotlin", ContractPreference.Employee, true, 150_000m, "java\r\n\r\n  kotlin  \r\n", "manager\r\n"), CancellationToken.None);
        JobHunter.Domain.Settings stored = await settingsService.GetAsync(CancellationToken.None);

        Assert.Equal("DE", stored.HomeCountryIso);
        Assert.Equal("Berlin", stored.HomeCity);
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
    public async Task SaveChangesAsync_ForASourceListStoredWithoutDescriptionHashes_LoadsItAndKeepsTheHashRecordedAfterwards()
    {
        await using ServiceProvider provider = BuildProvider();
        await provider.GetRequiredService<DatabaseInitializer>().InitializeAsync(CancellationToken.None);
        IDbContextFactory<JobHunterDbContext> contextFactory = provider.GetRequiredService<IDbContextFactory<JobHunterDbContext>>();
        Guid jobId = Guid.CreateVersion7();
        DateTimeOffset seenAt = new(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);

        await using (JobHunterDbContext writeContext = await contextFactory.CreateDbContextAsync(CancellationToken.None))
        {
            await InsertJobWithSourcesWithoutDescriptionHashesAsync(writeContext, jobId);
            Job legacy = await writeContext.Jobs.SingleAsync(candidate => candidate.Id == jobId, CancellationToken.None);

            Assert.Equal(2, legacy.Sources.Count);
            Assert.All(legacy.Sources, reference => Assert.Null(reference.DescriptionHash));
            Assert.Equal(new JobSourceRef(JobSourceKind.RemoteOk, "remoteok-1", seenAt), legacy.Sources[0]);

            legacy.RecordSource(JobSourceKind.RemoteOk, "remoteok-1", seenAt.AddDays(1), "Plain text description.", "hash-2");
            await writeContext.SaveChangesAsync(CancellationToken.None);
        }

        await using JobHunterDbContext readContext = await contextFactory.CreateDbContextAsync(CancellationToken.None);
        Job stored = await readContext.Jobs.AsNoTracking().SingleAsync(candidate => candidate.Id == jobId, CancellationToken.None);

        Assert.Equal("hash-2", stored.Sources[0].DescriptionHash);
        Assert.Null(stored.Sources[1].DescriptionHash);
        Assert.Equal(ScoringState.Scored, stored.Scoring);
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
        TestDataFolder.Delete(dataFolder);
    }

    private static ScoreCard NewScoreCard(DateTimeOffset scoredAt)
    {
        return new ScoreCard(2, 2, 2, 1, 2, 2, 1, 12, "Strong platform fit.", "senior", "remote", "b2b", null, null, null, null, "Overlaps European hours.", false, true, "Agent tooling.", ["timezone"], "claude-opus-5", scoredAt, "hash-1");
    }

    /// <summary>Writes a scored job whose source list is in the shape stored before each reference carried a description hash.</summary>
    private static async Task InsertJobWithSourcesWithoutDescriptionHashesAsync(JobHunterDbContext context, Guid jobId)
    {
        string id = jobId.ToString().ToUpperInvariant();
        const string SeenAt = "2026-09-20 08:00:00.0000000";
        const string Sources = """[{"Kind":"RemoteOk","LastSeenAt":"2026-09-20 08:00:00.0000000+00:00","SourceId":"remoteok-1"},{"Kind":"WeWorkRemotely","LastSeenAt":"2026-09-20 08:00:00.0000000+00:00","SourceId":"https://weworkremotely.com/remote-jobs/northwind-backend-engineer"}]""";

        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO Jobs (Id, Fingerprint, CanonicalApplyUrl, PostingUrl, Company, Title, DescriptionText, DescriptionHash, Tags,
                FirstSeenAt, LastSeenAt, IsActive, MissedRuns, IsManual, Prefilter, Flags, Scoring, Triage, Sources)
            VALUES ({id}, 'fingerprint-1', 'https://jobs.example.com/backend', 'https://jobs.example.com/backend', 'Northwind', 'Backend Engineer', 'Plain text description.', 'hash-1', '[]',
                {SeenAt}, {SeenAt}, 1, 0, 0, 'Passed', '[]', 'Scored', 'New', {Sources})
            """);
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
