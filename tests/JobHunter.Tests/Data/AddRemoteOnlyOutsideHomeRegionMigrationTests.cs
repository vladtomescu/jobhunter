using System.Data.Common;
using JobHunter.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace JobHunter.Tests.Data;

/// <summary>Proves that the home region rule migration turns the rule on for a settings row stored before it, keeps the rest of the row, and reverses cleanly.</summary>
public sealed class AddRemoteOnlyOutsideHomeRegionMigrationTests : IDisposable
{
    private const string MigrationBeforeTheRule = "20261018104047_AddSkills";

    private const string TheRuleMigration = "20261018135558_AddRemoteOnlyOutsideHomeRegion";

    private readonly string databaseFolder = Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task MigrateAsync_OverASettingsRowStoredBeforeTheRule_LeavesTheRuleOff()
    {
        await using JobHunterDbContext context = NewContext();
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBeforeTheRule);
        await InsertSettingsInThePreviousShapeAsync(context);

        await migrator.MigrateAsync(TheRuleMigration);

        DbConnection connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT RemoteOnlyOutsideHomeRegion, HomeCountryIso, FirstName FROM Settings";
        await using DbDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0, reader.GetInt64(0));
        Assert.Equal("NL", reader.GetString(1));
        Assert.Equal("Ada", reader.GetString(2));
    }

    [Fact]
    public async Task MigrateAsync_BackToBeforeTheRule_DropsTheColumnAndKeepsTheRow()
    {
        await using JobHunterDbContext context = NewContext();
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBeforeTheRule);
        await InsertSettingsInThePreviousShapeAsync(context);
        await migrator.MigrateAsync();

        await migrator.MigrateAsync(MigrationBeforeTheRule);

        DbConnection connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT FirstName, (SELECT COUNT(*) FROM pragma_table_info('Settings') WHERE name = 'RemoteOnlyOutsideHomeRegion') FROM Settings";
        await using DbDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("Ada", reader.GetString(0));
        Assert.Equal(0, reader.GetInt64(1));
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

    private static async Task InsertSettingsInThePreviousShapeAsync(JobHunterDbContext context)
    {
        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO Settings (Id, FirstName, LastName, Email, Phone, Location, LinkedInUrl, ResumePdfPath, ResumeMarkdownPath,
                MinContractorHourly, MinEmploymentAnnual, TargetAnnual, ScoreModel, KitModel, RemoteOkEnabled, WwrEnabled, DatasetEnabled, DatasetAtsList,
                FirstRunWindowDays, GhostThresholdDays, AutoRefreshAfterHours, MaxScoresPerRun, AcceptUnitedStatesRemote, KeepOnsiteWithCompOrRelocation, FxOverridesJson, UpdatedAt,
                AcceptEuropeRemote, AcceptedLanguages, BaseCurrency, CompComputedInCurrency, ContractPreference, HasUnitedStatesWorkAuthorization, HighPayThresholdPerYear,
                HomeCountryIso, HomeCity, StackKeywords, TitleExcludeTerms, TitleIncludeTerms)
            VALUES (1, 'Ada', 'Lovelace', 'ada@example.com', '', '', '', 'resume.pdf', 'resume.md',
                NULL, NULL, NULL, 'claude-opus-5', 'claude-opus-5', 1, 1, 0, 'greenhouse',
                21, 21, 12, 0, 1, 1, NULL, '2026-10-18 08:00:00.0000000',
                1, 'en', 'EUR', 'EUR', 'Either', 0, NULL,
                'NL', 'Utrecht', '', '', '')
            """);
    }
}
