using System.Data.Common;
using JobHunter.Data;
using JobHunter.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace JobHunter.Tests.Data;

/// <summary>Proves that the home city migration renames the stored home-country flag to the home-city flag, gives an existing settings row an empty home city, and reverses cleanly.</summary>
public sealed class RecognizeHomeCityMigrationTests : IDisposable
{
    private const string MigrationBeforeHomeCity = "20260924161731_GenericCandidateSettings";

    private readonly string databaseFolder = Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task MigrateAsync_OverAJobFlaggedInTheHomeCountry_RenamesTheFlagToTheHomeCity()
    {
        await using JobHunterDbContext context = NewContext();
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBeforeHomeCity);
        Guid jobId = Guid.CreateVersion7();
        await InsertJobInThePreviousShapeAsync(context, jobId, """["H4","HomeCountry"]""");

        await migrator.MigrateAsync();

        Job job = await context.Jobs.AsNoTracking().SingleAsync(candidate => candidate.Id == jobId);
        Assert.Equal<JobFlag>([JobFlag.H4, JobFlag.HomeCity], job.Flags);
    }

    [Fact]
    public async Task MigrateAsync_OverASettingsRowStoredBeforeTheHomeCity_LeavesTheHomeCityEmpty()
    {
        await using JobHunterDbContext context = NewContext();
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBeforeHomeCity);
        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO Settings (Id, FirstName, LastName, Email, Phone, Location, LinkedInUrl, ResumePdfPath, ResumeMarkdownPath,
                MinContractorHourly, MinEmploymentAnnual, TargetAnnual, ScoreModel, KitModel, RemoteOkEnabled, WwrEnabled, DatasetEnabled, DatasetAtsList,
                FirstRunWindowDays, GhostThresholdDays, AutoRefreshAfterHours, MaxScoresPerRun, AcceptUnitedStatesRemote, KeepOnsiteWithCompOrRelocation, FxOverridesJson, UpdatedAt,
                AcceptEuropeRemote, AcceptedLanguages, BaseCurrency, CompComputedInCurrency, ContractPreference, HasUnitedStatesWorkAuthorization, HighPayThresholdPerYear,
                HomeCountryIso, StackKeywords, TitleExcludeTerms, TitleIncludeTerms)
            VALUES (1, 'Ada', 'Lovelace', 'ada@example.com', '', '', '', 'resume.pdf', 'resume.md',
                NULL, NULL, NULL, 'claude-opus-5', 'claude-opus-5', 1, 1, 0, 'greenhouse',
                21, 21, 12, 0, 1, 1, NULL, '2026-09-20 08:00:00.0000000',
                1, 'en', 'EUR', 'EUR', 'Either', 0, NULL,
                'NL', '', '', '')
            """);

        await migrator.MigrateAsync();

        JobHunter.Domain.Settings settings = await context.Settings.AsNoTracking().SingleAsync();
        Assert.Equal(string.Empty, settings.HomeCity);
        Assert.Equal("NL", settings.HomeCountryIso);
        Assert.Equal("Ada", settings.FirstName);
    }

    [Fact]
    public async Task MigrateAsync_BackToBeforeTheHomeCity_RestoresTheHomeCountryFlag()
    {
        await using JobHunterDbContext context = NewContext();
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBeforeHomeCity);
        Guid jobId = Guid.CreateVersion7();
        await InsertJobInThePreviousShapeAsync(context, jobId, """["H4","HomeCountry"]""");
        await migrator.MigrateAsync();

        await migrator.MigrateAsync(MigrationBeforeHomeCity);

        DbConnection connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Flags FROM Jobs";
        await using DbDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("""["H4","HomeCountry"]""", reader.GetString(0));
    }

    public void Dispose()
    {
        TestDataFolder.Delete(databaseFolder);
    }

    private JobHunterDbContext NewContext()
    {
        Directory.CreateDirectory(databaseFolder);
        DbContextOptions<JobHunterDbContext> options = new DbContextOptionsBuilder<JobHunterDbContext>()
            .UseSqlite($"Data Source={Path.Combine(databaseFolder, "jobhunter.db")}")
            .Options;

        return new JobHunterDbContext(options);
    }

    private static async Task InsertJobInThePreviousShapeAsync(JobHunterDbContext context, Guid jobId, string flagsJson)
    {
        string id = jobId.ToString().ToUpperInvariant();
        const string SeenAt = "2026-09-20 08:00:00.0000000";

        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO Jobs (Id, Fingerprint, CanonicalApplyUrl, PostingUrl, Company, Title, DescriptionText, DescriptionHash, Tags,
                CompMin, CompMax, CompCurrency, CompPeriod, CompMinPerYear, CompMaxPerYear, FirstSeenAt, LastSeenAt,
                IsActive, MissedRuns, IsManual, Prefilter, Flags, Scoring, Triage, Sources)
            VALUES ({id}, 'fingerprint-1', 'https://jobs.example.com/backend', 'https://jobs.example.com/backend', 'Example', 'Senior Backend Engineer', 'Hybrid in the office.', 'hash-1', '[]',
                NULL, NULL, NULL, NULL, NULL, NULL, {SeenAt}, {SeenAt},
                1, 0, 0, 'Passed', {flagsJson}, 'Unscored', 'New', '[]')
            """);
    }
}
