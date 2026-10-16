using System.Collections.Concurrent;
using System.Globalization;
using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobHunter.Tests.Data;

/// <summary>A clock the test sets by hand, whose timers tick only when the test says so.</summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly ConcurrentQueue<ManualTimer> timers = new();

    /// <summary>The time the clock reports.</summary>
    public DateTimeOffset UtcNow { get; set; } = start;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        return UtcNow;
    }

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ManualTimer timer = new(callback, state);
        timers.Enqueue(timer);

        return timer;
    }

    /// <summary>Fires every timer created so far once, as if its period had passed.</summary>
    public void Tick()
    {
        foreach (ManualTimer timer in timers)
        {
            timer.Fire();
        }
    }

    private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
    {
        public void Fire()
        {
            callback(state);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            return true;
        }

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>A logger that keeps the level of every entry, so a test can see that a failure was logged as a warning.</summary>
internal sealed class LevelRecordingLogger<T> : ILogger<T>
{
    /// <summary>The level of every entry, in order.</summary>
    public ConcurrentQueue<LogLevel> Levels { get; } = new();

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Levels.Enqueue(logLevel);
    }
}

/// <summary>Proves that the daily copy of the database is a complete, healthy database written once per UTC day, that the folder keeps the newest seven dated copies, that everything else in it is left alone and that a failed copy is logged without leaving a file behind.</summary>
public sealed class DatabaseCopyWriterTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Today = new(2026, 3, 10, 0, 30, 0, TimeSpan.Zero);

    private readonly string dataFolder = Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider clock = new(Today);
    private readonly ServiceProvider provider;

    public DatabaseCopyWriterTests()
    {
        ServiceCollection services = new();
        services.AddData();
        services.AddSettings();
        services.AddSingleton(new DataPaths(dataFolder));
        services.AddSingleton<TimeProvider>(clock);
        provider = services.BuildServiceProvider();
    }

    private DatabaseCopyWriter Writer => provider.GetRequiredService<DatabaseCopyWriter>();

    private string CopiesFolder => provider.GetRequiredService<DataPaths>().DatabaseCopies;

    public Task InitializeAsync()
    {
        return provider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await provider.DisposeAsync();
        TestDataFolder.Delete(dataFolder);
    }

    [Fact]
    public async Task WriteTodaysCopyAsync_OnADatabaseWithRows_WritesACopyThatPassesTheIntegrityCheckWithTheSameRowCounts()
    {
        await SeedAsync();

        string? copyFile = await Writer.WriteTodaysCopyAsync();

        Assert.NotNull(copyFile);
        Assert.Equal(Path.Combine(CopiesFolder, "jobhunter-2026-03-10.db"), copyFile);
        await using SqliteConnection copy = await OpenReadOnlyAsync(copyFile);
        Assert.Equal("ok", await ScalarAsync(copy, "PRAGMA integrity_check"));
        Assert.Equal("delete", await ScalarAsync(copy, "PRAGMA journal_mode"));
        Dictionary<string, long> sourceCounts = await RowCountsAsync(provider.GetRequiredService<DataPaths>().DatabaseFile);
        Assert.Equal(sourceCounts, await RowCountsAsync(copyFile));
        Assert.Equal(2, sourceCounts["Jobs"]);
        Assert.Equal(1, sourceCounts["Applications"]);
        Assert.Equal(1, sourceCounts["FetchRuns"]);
    }

    [Fact]
    public async Task WriteTodaysCopyAsync_WithTheDatabaseInAFolderOfItsOwn_WritesAHealthyCopyOfThatDatabaseIntoTheDataRootBackupFolder()
    {
        string separateRoot = Path.GetFullPath(Path.Combine(dataFolder, "separate-root"));
        string databaseFolder = Path.GetFullPath(Path.Combine(dataFolder, "separate-database"));
        ServiceCollection services = new();
        services.AddData();
        services.AddSettings();
        services.AddSingleton(new DataPaths(separateRoot, databaseFolder));
        services.AddSingleton<TimeProvider>(clock);
        ServiceProvider separateProvider = services.BuildServiceProvider();

        try
        {
            await separateProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();

            string? copyFile = await separateProvider.GetRequiredService<DatabaseCopyWriter>().WriteTodaysCopyAsync();

            Assert.NotNull(copyFile);
            Assert.Equal(Path.Combine(separateRoot, "backup", "jobhunter-2026-03-10.db"), copyFile);
            Assert.True(File.Exists(Path.Combine(databaseFolder, "jobhunter.db")));
            Assert.False(File.Exists(Path.Combine(separateRoot, "jobhunter.db")));
            await using SqliteConnection copy = await OpenReadOnlyAsync(copyFile);
            Assert.Equal("ok", await ScalarAsync(copy, "PRAGMA integrity_check"));
            Assert.Equal(await RowCountsAsync(Path.Combine(databaseFolder, "jobhunter.db")), await RowCountsAsync(copyFile));
        }
        finally
        {
            await separateProvider.DisposeAsync();
            TestDataFolder.Delete(databaseFolder);
        }
    }

    [Fact]
    public async Task WriteTodaysCopyAsync_AfterACopy_LeavesNoTemporaryFile()
    {
        await Writer.WriteTodaysCopyAsync();

        Assert.Equal(["jobhunter-2026-03-10.db"], FileNames(CopiesFolder));
    }

    [Fact]
    public async Task WriteTodaysCopyAsync_CalledAgainLaterTheSameUtcDay_WritesNothing()
    {
        string? firstCopy = await Writer.WriteTodaysCopyAsync();
        Assert.NotNull(firstCopy);
        DateTime firstWrittenAt = File.GetLastWriteTimeUtc(firstCopy);
        await SeedAsync();
        clock.UtcNow = Today.AddHours(23);

        string? secondCopy = await Writer.WriteTodaysCopyAsync();

        Assert.Null(secondCopy);
        Assert.Equal(firstWrittenAt, File.GetLastWriteTimeUtc(firstCopy));
        Assert.Equal(["jobhunter-2026-03-10.db"], FileNames(CopiesFolder));
    }

    [Fact]
    public async Task WriteTodaysCopyAsync_WithSevenOlderCopies_DeletesTheOldestSoTheNewestSevenRemain()
    {
        foreach (int day in Enumerable.Range(3, 7))
        {
            await File.WriteAllTextAsync(Path.Combine(CopiesFolder, $"jobhunter-2026-03-{day:00}.db"), "an older copy");
        }

        await Writer.WriteTodaysCopyAsync();

        Assert.Equal([.. Enumerable.Range(4, 7).Select(day => $"jobhunter-2026-03-{day:00}.db")], FileNames(CopiesFolder));
    }

    [Fact]
    public async Task WriteTodaysCopyAsync_WithOtherFilesAndAFolderBesideTheCopies_LeavesThemAlone()
    {
        foreach (int day in Enumerable.Range(1, 9))
        {
            await File.WriteAllTextAsync(Path.Combine(CopiesFolder, $"jobhunter-2026-03-{day:00}.db"), "an older copy");
        }

        await File.WriteAllTextAsync(Path.Combine(CopiesFolder, "jobhunter-before-something.db"), "a copy made by hand");
        await File.WriteAllTextAsync(Path.Combine(CopiesFolder, "notes.txt"), "notes");
        string handFolder = Directory.CreateDirectory(Path.Combine(CopiesFolder, "2026-01-01-before-cleanup")).FullName;
        await File.WriteAllTextAsync(Path.Combine(handFolder, "jobhunter-2026-01-01.db"), "a copy kept in a folder");

        await Writer.WriteTodaysCopyAsync();

        Assert.Equal(
            ["jobhunter-2026-03-04.db", "jobhunter-2026-03-05.db", "jobhunter-2026-03-06.db", "jobhunter-2026-03-07.db", "jobhunter-2026-03-08.db", "jobhunter-2026-03-09.db", "jobhunter-2026-03-10.db", "jobhunter-before-something.db", "notes.txt"],
            FileNames(CopiesFolder));
        Assert.Equal("a copy made by hand", await File.ReadAllTextAsync(Path.Combine(CopiesFolder, "jobhunter-before-something.db")));
        Assert.Equal(["jobhunter-2026-01-01.db"], FileNames(handFolder));
    }

    [Fact]
    public async Task CheckAsync_WhenTheFinishedCopyCannotTakeItsName_LogsAWarningAndLeavesNoFileBehind()
    {
        Directory.CreateDirectory(Path.Combine(CopiesFolder, "jobhunter-2026-03-10.db"));
        LevelRecordingLogger<DailyDatabaseCopyHostedService> logger = new();
        using DailyDatabaseCopyHostedService service = new(Writer, clock, logger);

        await service.CheckAsync(CancellationToken.None);

        Assert.Equal([LogLevel.Warning], logger.Levels);
        Assert.Empty(FileNames(CopiesFolder));
    }

    [Fact]
    public async Task ExecuteAsync_LeftRunningPastMidnightUtc_WritesTodaysCopyAtStartAndTheNextDaysCopyOnALaterCheck()
    {
        clock.UtcNow = Today.AddHours(23);
        using DailyDatabaseCopyHostedService service = new(Writer, clock, NullLogger<DailyDatabaseCopyHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await WaitForCopyAsync("jobhunter-2026-03-10.db");
        clock.UtcNow = Today.AddDays(1);
        clock.Tick();
        await WaitForCopyAsync("jobhunter-2026-03-11.db");
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(["jobhunter-2026-03-10.db", "jobhunter-2026-03-11.db"], FileNames(CopiesFolder));
    }

    private async Task SeedAsync()
    {
        DateTimeOffset at = Today.AddDays(-1);
        Job saved = Job.Create("fingerprint-saved", "https://jobs.example.com/saved", "https://jobs.example.com/saved", "Examplecorp", "Platform Engineer", "A description.", "hash-saved", at, isManual: true);
        Job other = Job.Create("fingerprint-other", "https://jobs.example.com/other", "https://jobs.example.com/other", "Othercorp", "Backend Engineer", "Another description.", "hash-other", at, isManual: true);

        await using JobHunterDbContext context = await provider.GetRequiredService<IDbContextFactory<JobHunterDbContext>>().CreateDbContextAsync();
        context.Jobs.AddRange(saved, other);
        context.Applications.Add(Application.Create(saved.Id, ApplicationStatus.Saved, at, "Saved from the inbox."));
        context.FetchRuns.Add(FetchRun.Start(FetchTrigger.Manual, at));
        await context.SaveChangesAsync();
    }

    private async Task WaitForCopyAsync(string fileName)
    {
        string copyFile = Path.Combine(CopiesFolder, fileName);

        for (int attempt = 0; attempt < 500 && !File.Exists(copyFile); attempt++)
        {
            await Task.Delay(20);
        }

        Assert.True(File.Exists(copyFile), $"{fileName} was not written.");
    }

    private static string[] FileNames(string folder)
    {
        return [.. Directory.GetFiles(folder).Select(file => Path.GetFileName(file)).Order(StringComparer.Ordinal)];
    }

    private static async Task<SqliteConnection> OpenReadOnlyAsync(string databaseFile)
    {
        SqliteConnection connection = new($"Data Source={databaseFile};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync();

        return connection;
    }

    private static async Task<string?> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<Dictionary<string, long>> RowCountsAsync(string databaseFile)
    {
        await using SqliteConnection connection = await OpenReadOnlyAsync(databaseFile);
        List<string> tables = [];

        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";
            await using SqliteDataReader reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        Dictionary<string, long> counts = [];

        foreach (string table in tables)
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
            counts[table] = (long)(await command.ExecuteScalarAsync())!;
        }

        return counts;
    }
}
