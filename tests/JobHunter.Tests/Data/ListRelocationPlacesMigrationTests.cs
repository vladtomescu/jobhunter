using System.Data.Common;
using JobHunter.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace JobHunter.Tests.Data;

/// <summary>Proves that the relocation places migration drops the accepted remote regions and the two geography switches, gives a settings row stored before it an empty list of places and keeps the rest of the row, and reverses to the defaults a new install had.</summary>
public sealed class ListRelocationPlacesMigrationTests : IDisposable
{
    private const string MigrationBeforeThePlaces = "20261018201558_AddExcludedCompanies";

    private readonly string databaseFolder = Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task MigrateAsync_OverASettingsRowStoredBeforeThePlaces_GivesItNoPlacesAndKeepsTheRest()
    {
        await using JobHunterDbContext context = NewContext();
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBeforeThePlaces);
        await InsertSettingsInThePreviousShapeAsync(context);

        await migrator.MigrateAsync();

        JobHunter.Domain.Settings settings = await context.Settings.AsNoTracking().SingleAsync();
        Assert.Equal(string.Empty, settings.RelocationPlaces);
        Assert.Equal("NL", settings.HomeCountryIso);
        Assert.Equal("Utrecht", settings.HomeCity);
        Assert.Equal("en,nl", settings.AcceptedLanguages);
        Assert.Equal("Initrode", settings.ExcludedCompanies);
        Assert.Equal(55m, settings.MinContractorHourly);
        Assert.Equal("Ada", settings.FirstName);
    }

    [Fact]
    public async Task MigrateAsync_OverASettingsRowStoredBeforeThePlaces_DropsTheFourGeographyColumns()
    {
        await using JobHunterDbContext context = NewContext();
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBeforeThePlaces);
        await InsertSettingsInThePreviousShapeAsync(context);

        await migrator.MigrateAsync();

        DbConnection connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT COUNT(*) FROM pragma_table_info('Settings') WHERE name IN ('AcceptEuropeRemote', 'AcceptUnitedStatesRemote', 'KeepOnsiteWithCompOrRelocation', 'RemoteOnlyOutsideHomeRegion')), (SELECT COUNT(*) FROM pragma_table_info('Settings') WHERE name = 'RelocationPlaces')";
        await using DbDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0, reader.GetInt64(0));
        Assert.Equal(1, reader.GetInt64(1));
    }

    [Fact]
    public async Task MigrateAsync_BackToBeforeThePlaces_RestoresTheFourColumnsAtTheNewInstallDefaultsAndKeepsTheRow()
    {
        await using JobHunterDbContext context = NewContext();
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBeforeThePlaces);
        await InsertSettingsInThePreviousShapeAsync(context);
        await migrator.MigrateAsync();

        await migrator.MigrateAsync(MigrationBeforeThePlaces);

        DbConnection connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT FirstName, AcceptEuropeRemote, AcceptUnitedStatesRemote, KeepOnsiteWithCompOrRelocation, RemoteOnlyOutsideHomeRegion, (SELECT COUNT(*) FROM pragma_table_info('Settings') WHERE name = 'RelocationPlaces') FROM Settings";
        await using DbDataReader reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("Ada", reader.GetString(0));
        Assert.Equal(1, reader.GetInt64(1));
        Assert.Equal(1, reader.GetInt64(2));
        Assert.Equal(1, reader.GetInt64(3));
        Assert.Equal(0, reader.GetInt64(4));
        Assert.Equal(0, reader.GetInt64(5));
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

    /// <summary>A settings row with every geography switch set away from its new-install default, so the reversal shows the defaults rather than the stored values.</summary>
    private static async Task InsertSettingsInThePreviousShapeAsync(JobHunterDbContext context)
    {
        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO Settings (Id, FirstName, LastName, Email, Phone, Location, LinkedInUrl, ResumePdfPath, ResumeMarkdownPath,
                MinContractorHourly, MinEmploymentAnnual, TargetAnnual, ScoreModel, KitModel, RemoteOkEnabled, WwrEnabled, DatasetEnabled, DatasetAtsList,
                FirstRunWindowDays, GhostThresholdDays, AutoRefreshAfterHours, MaxScoresPerRun, AcceptUnitedStatesRemote, KeepOnsiteWithCompOrRelocation, FxOverridesJson, UpdatedAt,
                AcceptEuropeRemote, AcceptedLanguages, BaseCurrency, CompComputedInCurrency, ContractPreference, HasUnitedStatesWorkAuthorization, HighPayThresholdPerYear,
                HomeCountryIso, HomeCity, StackKeywords, TitleExcludeTerms, TitleIncludeTerms, RemoteOnlyOutsideHomeRegion, ExcludedCompanies)
            VALUES (1, 'Ada', 'Lovelace', 'ada@example.com', '', '', '', 'resume.pdf', 'resume.md',
                55, NULL, NULL, 'claude-opus-5', 'claude-opus-5', 1, 1, 0, 'greenhouse',
                21, 21, 12, 0, 0, 0, NULL, '2026-10-22 08:00:00.0000000',
                0, 'en,nl', 'EUR', 'EUR', 'Either', 0, NULL,
                'NL', 'Utrecht', '', '', '', 1, 'Initrode')
            """);
    }
}
