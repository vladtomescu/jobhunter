using System.Data.Common;
using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Tests.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace JobHunter.Tests.Data;

/// <summary>Proves that the skills migration seeds the common skills with their aliases and no CV mark, that a score card stored before skills reads with none and is marked as scored before skills, and that it reverses cleanly.</summary>
public sealed class AddSkillsMigrationTests : IDisposable
{
    private const string MigrationBeforeSkills = "20261002060145_NameStatusesAfterInterviewRounds";

    private readonly string databaseFolder = Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task MigrateAsync_OverADatabaseBeforeSkills_SeedsTheCommonSkillsWithTheirAliasesAndNoMark()
    {
        await using JobHunterDbContext context = NewContext();
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBeforeSkills);

        await migrator.MigrateAsync();

        List<Skill> skills = await context.Skills.AsNoTracking().OrderBy(skill => skill.Id).ToListAsync();
        Assert.Equal<string>([".NET", "AWS", "Go", "Kubernetes", "PostgreSQL", "JavaScript", "TypeScript"], [.. skills.Select(skill => skill.Name)]);
        Assert.Equal<string>([".net", "dotnet", ".net core", "asp.net core", "c#"], skills[0].Aliases);
        Assert.Contains("golang", skills[2].Aliases);
        Assert.Equal(SkillCategory.Database, skills[4].Category);
        Assert.All(skills, skill => Assert.Null(skill.CvMark));
        Assert.All(skills, skill => Assert.Null(skill.CvMarkSetBy));
    }

    [Fact]
    public async Task MigrateAsync_OverAJobScoredBeforeSkills_ReadsItsScoreCardWithNoSkillsMarkedAsScoredBeforeSkills()
    {
        await using JobHunterDbContext context = NewContext();
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBeforeSkills);
        Job job = ListedJobs.NewInboxJob("Example", JobClass.A, null, new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero));
        context.Jobs.Add(job);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        await context.Database.ExecuteSqlAsync($"UPDATE Jobs SET Score = json_remove(Score, '$.Skills')");

        await migrator.MigrateAsync();

        ScoreCard score = (await context.Jobs.AsNoTracking().SingleAsync(candidate => candidate.Id == job.Id)).Score!;
        Assert.Empty(score.Skills);
        Assert.True(score.ScoredBeforeSkills);
        Assert.Equal("senior", score.LevelGuess);
    }

    [Fact]
    public async Task MigrateAsync_BackToBeforeSkills_DropsTheSkillsTableAndTheSkillsOfEveryCard()
    {
        await using JobHunterDbContext context = NewContext();
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync();
        Job job = ListedJobs.NewInboxJob("Example", JobClass.A, null, new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero));
        job.RecordScore(job.Score! with { Skills = [new ScoreSkill("Go", "primary", "language", "yes")], ScoredBeforeSkills = true }, JobClass.A);
        context.Jobs.Add(job);
        await context.SaveChangesAsync();

        await migrator.MigrateAsync(MigrationBeforeSkills);

        DbConnection connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using DbCommand tables = connection.CreateCommand();
        tables.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Skills'";
        Assert.Equal(0L, (long)(await tables.ExecuteScalarAsync())!);
        await using DbCommand cards = connection.CreateCommand();
        cards.CommandText = "SELECT json_type(Score, '$.Skills') IS NULL AND json_type(Score, '$.ScoredBeforeSkills') IS NULL AND json_type(Score, '$.LevelGuess') = 'text' FROM Jobs";
        Assert.Equal(1L, (long)(await cards.ExecuteScalarAsync())!);
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
}
