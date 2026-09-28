using System.Data.Common;
using JobHunter.Data;
using JobHunter.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace JobHunter.Tests.Data;

/// <summary>Proves that the cover-letter migration leaves an application stored before it without a letter, lets a letter be stored afterwards, and reverses cleanly.</summary>
public sealed class StoreCoverLettersMigrationTests : IDisposable
{
    private const string MigrationBeforeCoverLetters = "20260927084608_RecognizeHomeCity";

    private readonly string databaseFolder = Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task MigrateAsync_OverAnApplicationStoredBeforeCoverLetters_ReadsItWithoutALetterAndStoresOneAfterwards()
    {
        await using JobHunterDbContext context = NewContext();
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBeforeCoverLetters);
        Guid applicationId = Guid.CreateVersion7();
        await InsertApplicationInThePreviousShapeAsync(context, applicationId);

        await migrator.MigrateAsync();

        Application application = await context.Applications.SingleAsync(candidate => candidate.Id == applicationId);
        Assert.Null(application.CoverLetter);
        Assert.Equal(ApplicationStatus.Saved, application.Status);

        application.AttachCoverLetter(new ApplicationCoverLetter("en", "Dear Example Co team,", ["One.", "Two.", "Three."], "Kind regards,", new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero), "claude-code", []));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        ApplicationCoverLetter stored = (await context.Applications.AsNoTracking().SingleAsync(candidate => candidate.Id == applicationId)).CoverLetter!;
        Assert.Equal<string>(["One.", "Two.", "Three."], stored.Paragraphs);
        Assert.Equal("claude-code", stored.Model);
    }

    [Fact]
    public async Task MigrateAsync_BackToBeforeCoverLetters_DropsTheColumnAndKeepsTheApplication()
    {
        await using JobHunterDbContext context = NewContext();
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBeforeCoverLetters);
        Guid applicationId = Guid.CreateVersion7();
        await InsertApplicationInThePreviousShapeAsync(context, applicationId);
        await migrator.MigrateAsync();

        await migrator.MigrateAsync(MigrationBeforeCoverLetters);

        DbConnection connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using DbCommand columns = connection.CreateCommand();
        columns.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Applications') WHERE name = 'CoverLetter'";
        Assert.Equal(0L, (long)(await columns.ExecuteScalarAsync())!);
        await using DbCommand rows = connection.CreateCommand();
        rows.CommandText = "SELECT COUNT(*) FROM Applications";
        Assert.Equal(1L, (long)(await rows.ExecuteScalarAsync())!);
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

    private static async Task InsertApplicationInThePreviousShapeAsync(JobHunterDbContext context, Guid applicationId)
    {
        string id = applicationId.ToString().ToUpperInvariant();
        string jobId = Guid.CreateVersion7().ToString().ToUpperInvariant();
        const string ChangedAt = "2026-09-20 08:00:00.0000000";
        const string History = """[{"At":"2026-09-20T08:00:00+00:00","Note":"Saved from the inbox.","Status":"Saved"}]""";

        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO Applications (Id, JobId, Status, StatusChangedAt, KitState, History, Notes)
            VALUES ({id}, {jobId}, 'Saved', {ChangedAt}, 'None', {History}, '[]')
            """);
    }
}
