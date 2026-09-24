using System.Data.Common;
using JobHunter.Data;
using JobHunter.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace JobHunter.Tests.Data;

/// <summary>Proves that the candidate settings migration renames the comp columns without touching the values or the stored flags, gives an existing settings row the neutral defaults, and reverses cleanly.</summary>
public sealed class GenericCandidateSettingsMigrationTests : IDisposable
{
    private const string MigrationBeforeCandidateSettings = "20260924075641_RecordScoringFailureReasons";

    private readonly string databaseFolder = Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task MigrateAsync_OverRowsStoredBeforeTheCandidateSettings_KeepsTheFlagsAndTheComp()
    {
        await using JobHunterDbContext context = NewContext();
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBeforeCandidateSettings);
        Guid jobId = Guid.CreateVersion7();
        await InsertJobInThePreviousShapeAsync(context, jobId, """["H1","H3"]""", 88_000d, 110_000d);
        await InsertSettingsInThePreviousShapeAsync(context, keepUsOnlyRemote: false);

        await migrator.MigrateAsync();

        Job job = await context.Jobs.AsNoTracking().SingleAsync(candidate => candidate.Id == jobId);
        Assert.Equal<JobFlag>([JobFlag.H1, JobFlag.H3], job.Flags);
        Assert.Equal(88_000m, job.CompMinPerYear);
        Assert.Equal(110_000m, job.CompMaxPerYear);
        Assert.Equal(80_000m, job.CompMin);
        Assert.Equal(100_000m, job.CompMax);
        Assert.Equal("USD", job.CompCurrency);
    }

    [Fact]
    public async Task MigrateAsync_OverASettingsRowStoredBeforeTheCandidateSettings_GivesItTheNeutralDefaultsAndKeepsTheRest()
    {
        await using JobHunterDbContext context = NewContext();
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBeforeCandidateSettings);
        await InsertSettingsInThePreviousShapeAsync(context, keepUsOnlyRemote: false);

        await migrator.MigrateAsync();

        JobHunter.Domain.Settings settings = await context.Settings.AsNoTracking().SingleAsync();
        JobHunter.Domain.Settings defaults = JobHunter.Domain.Settings.CreateDefault();
        Assert.Null(settings.HomeCountryIso);
        Assert.True(settings.AcceptEuropeRemote);
        Assert.False(settings.AcceptUnitedStatesRemote);
        Assert.Equal(defaults.AcceptedLanguages, settings.AcceptedLanguages);
        Assert.Equal(defaults.BaseCurrency, settings.BaseCurrency);
        Assert.Equal(defaults.BaseCurrency, settings.CompComputedInCurrency);
        Assert.Equal(string.Empty, settings.StackKeywords);
        Assert.Equal(ContractPreference.Either, settings.ContractPreference);
        Assert.False(settings.HasUnitedStatesWorkAuthorization);
        Assert.Null(settings.HighPayThresholdPerYear);
        Assert.Equal(defaults.TitleIncludeTerms, settings.TitleIncludeTerms);
        Assert.Equal(defaults.TitleExcludeTerms, settings.TitleExcludeTerms);
        Assert.Equal(55m, settings.MinContractorHourly);
        Assert.Equal(90_000m, settings.MinEmploymentAnnual);
        Assert.Equal(120_000m, settings.TargetAnnual);
        Assert.Equal("Ada", settings.FirstName);
        Assert.Equal(0, settings.MaxScoresPerRun);
        Assert.True(settings.KeepOnsiteWithCompOrRelocation);
    }

    [Fact]
    public async Task MigrateAsync_BackToBeforeTheCandidateSettings_RestoresTheOldColumns()
    {
        await using JobHunterDbContext context = NewContext();
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBeforeCandidateSettings);
        Guid jobId = Guid.CreateVersion7();
        await InsertJobInThePreviousShapeAsync(context, jobId, """["H1","H3"]""", 88_000d, 110_000d);
        await migrator.MigrateAsync();

        await migrator.MigrateAsync(MigrationBeforeCandidateSettings);

        DbConnection connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Flags, CompMinEurYear, CompMaxEurYear FROM Jobs";
        await using DbDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("""["H1","H3"]""", reader.GetString(0));
        Assert.Equal(88_000d, reader.GetDouble(1));
        Assert.Equal(110_000d, reader.GetDouble(2));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(databaseFolder))
        {
            Directory.Delete(databaseFolder, recursive: true);
        }
    }

    private JobHunterDbContext NewContext()
    {
        Directory.CreateDirectory(databaseFolder);
        DbContextOptions<JobHunterDbContext> options = new DbContextOptionsBuilder<JobHunterDbContext>()
            .UseSqlite($"Data Source={Path.Combine(databaseFolder, "jobhunter.db")}")
            .Options;

        return new JobHunterDbContext(options);
    }

    private static async Task InsertJobInThePreviousShapeAsync(JobHunterDbContext context, Guid jobId, string flagsJson, double compMinEurYear, double compMaxEurYear)
    {
        string id = jobId.ToString().ToUpperInvariant();
        const string SeenAt = "2026-09-20 08:00:00.0000000";

        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO Jobs (Id, Fingerprint, CanonicalApplyUrl, PostingUrl, Company, Title, DescriptionText, DescriptionHash, Tags,
                CompMin, CompMax, CompCurrency, CompPeriod, CompMinEurYear, CompMaxEurYear, FirstSeenAt, LastSeenAt,
                IsActive, MissedRuns, IsManual, Prefilter, Flags, Scoring, Triage, Sources)
            VALUES ({id}, 'fingerprint-1', 'https://jobs.example.com/backend', 'https://jobs.example.com/backend', 'Example', 'Senior C# Engineer', 'Hybrid in the office.', 'hash-1', '[]',
                80000, 100000, 'USD', 'Year', {compMinEurYear}, {compMaxEurYear}, {SeenAt}, {SeenAt},
                1, 0, 0, 'Passed', {flagsJson}, 'Unscored', 'New', '[]')
            """);
    }

    private static async Task InsertSettingsInThePreviousShapeAsync(JobHunterDbContext context, bool keepUsOnlyRemote)
    {
        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO Settings (Id, FirstName, LastName, Email, Phone, Location, LinkedInUrl, ResumePdfPath, ResumeMarkdownPath,
                MinB2bHourlyEur, MinEmploymentAnnualEur, TargetAnnualEur, ScoreModel, KitModel, RemoteOkEnabled, WwrEnabled, DatasetEnabled, DatasetAtsList,
                FirstRunWindowDays, GhostThresholdDays, AutoRefreshAfterHours, MaxScoresPerRun, KeepUsOnlyRemote, KeepOnsiteWithCompOrRelocation, FxOverridesJson, UpdatedAt)
            VALUES (1, 'Ada', 'Lovelace', 'ada@example.com', '', '', '', 'resume.pdf', 'resume.md',
                55, 90000, 120000, 'claude-opus-5', 'claude-opus-5', 1, 1, 0, 'greenhouse',
                21, 21, 12, 0, {keepUsOnlyRemote}, 1, NULL, '2026-09-20 08:00:00.0000000')
            """);
    }
}
