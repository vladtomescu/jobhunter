using System.Data.Common;
using JobHunter.Data;
using JobHunter.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace JobHunter.Tests.Data;

/// <summary>Proves that the round-status migration moves the old interview statuses onto the rounds in the status column and inside the stored history, so that an application stored before it still loads, leaves the other statuses and the notes alone, and maps the rounds back when reversed.</summary>
public sealed class NameStatusesAfterInterviewRoundsMigrationTests : IDisposable
{
    private const string MigrationBeforeRoundStatuses = "20260928131624_StoreCoverLetters";

    private readonly string databaseFolder = Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task MigrateAsync_OverAnApplicationWithTheOldInterviewStatusesInItsHistory_LoadsItWithTheRoundStatuses()
    {
        await using JobHunterDbContext context = NewContext();
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBeforeRoundStatuses);
        Guid applicationId = Guid.CreateVersion7();
        const string History = """[{"At":"2026-09-20 08:00:00+00:00","Note":"First interview booked.","Status":"Interview1"},{"At":"2026-09-24 08:00:00+00:00","Note":null,"Status":"Interview2"},{"At":"2026-09-27 08:01:11.9275641+00:00","Note":"Final round on Friday.","Status":"Final"}]""";
        await InsertApplicationInThePreviousShapeAsync(context, applicationId, "Interview1", History);

        await migrator.MigrateAsync();

        Application application = await context.Applications.AsNoTracking().SingleAsync(candidate => candidate.Id == applicationId);
        Assert.Equal(ApplicationStatus.Tech, application.Status);
        Assert.Equal<ApplicationStatus>([ApplicationStatus.Tech, ApplicationStatus.Tech, ApplicationStatus.Fit], [.. application.History.Select(entry => entry.Status)]);
        Assert.Equal(new[] { "First interview booked.", null, "Final round on Friday." }, application.History.Select(entry => entry.Note));
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.Zero), application.History[1].At);
    }

    [Theory]
    [InlineData("Interview1", ApplicationStatus.Tech)]
    [InlineData("Interview2", ApplicationStatus.Tech)]
    [InlineData("Final", ApplicationStatus.Fit)]
    [InlineData("Screening", ApplicationStatus.Screening)]
    [InlineData("Offer", ApplicationStatus.Offer)]
    public async Task MigrateAsync_OverAnApplicationAtAStatus_LoadsItAtTheStatusThatStatusBecomes(string storedStatus, ApplicationStatus expected)
    {
        await using JobHunterDbContext context = NewContext();
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBeforeRoundStatuses);
        Guid applicationId = Guid.CreateVersion7();
        await InsertApplicationInThePreviousShapeAsync(context, applicationId, storedStatus, $$"""[{"At":"2026-09-20 08:00:00+00:00","Note":null,"Status":"{{storedStatus}}"}]""");

        await migrator.MigrateAsync();

        Application application = await context.Applications.AsNoTracking().SingleAsync(candidate => candidate.Id == applicationId);
        Assert.Equal(expected, application.Status);
        Assert.Equal(expected, Assert.Single(application.History).Status);
    }

    [Fact]
    public async Task MigrateAsync_BackToBeforeTheRoundStatuses_MapsEveryRoundOntoAnOldInterviewStatus()
    {
        await using JobHunterDbContext context = NewContext();
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync();
        DateTimeOffset at = new(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);
        Dictionary<Guid, string> oldStatusByApplication = [];
        foreach ((ApplicationStatus round, string oldStatus) in new[] { (ApplicationStatus.Manager, "Interview1"), (ApplicationStatus.Tech, "Interview1"), (ApplicationStatus.SystemDesign, "Interview2"), (ApplicationStatus.Fit, "Final") })
        {
            Application application = Application.Create(Guid.CreateVersion7(), ApplicationStatus.Screening, at, null);
            application.MoveTo(round, at.AddDays(1), null);
            context.Applications.Add(application);
            oldStatusByApplication[application.Id] = oldStatus;
        }

        await context.SaveChangesAsync();

        await migrator.MigrateAsync(MigrationBeforeRoundStatuses);

        DbConnection connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Status, History FROM Applications";
        await using DbDataReader reader = await command.ExecuteReaderAsync();
        Dictionary<Guid, (string Status, string History)> stored = [];
        while (await reader.ReadAsync())
        {
            stored[Guid.Parse(reader.GetString(0))] = (reader.GetString(1), reader.GetString(2));
        }

        Assert.Equal(oldStatusByApplication.Count, stored.Count);
        foreach ((Guid applicationId, string oldStatus) in oldStatusByApplication)
        {
            Assert.Equal(oldStatus, stored[applicationId].Status);
            Assert.Contains($"\"Status\":\"{oldStatus}\"", stored[applicationId].History, StringComparison.Ordinal);
            Assert.Contains("\"Status\":\"Screening\"", stored[applicationId].History, StringComparison.Ordinal);
        }
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

    private static async Task InsertApplicationInThePreviousShapeAsync(JobHunterDbContext context, Guid applicationId, string status, string historyJson)
    {
        string id = applicationId.ToString().ToUpperInvariant();
        string jobId = Guid.CreateVersion7().ToString().ToUpperInvariant();
        const string ChangedAt = "2026-09-27 08:01:11.9275641";

        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO Applications (Id, JobId, Status, StatusChangedAt, KitState, History, Notes)
            VALUES ({id}, {jobId}, {status}, {ChangedAt}, 'None', {historyJson}, '[]')
            """);
    }
}
