using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Data;

/// <summary>Writes one copy of the database per UTC day into the database copies folder, named jobhunter-YYYY-MM-DD.db, and keeps the newest seven; files in that folder without that name are never touched.</summary>
public sealed partial class DatabaseCopyWriter(IDbContextFactory<JobHunterDbContext> contextFactory, DataPaths dataPaths, TimeProvider timeProvider)
{
    /// <summary>How many dated copies the folder keeps.</summary>
    public const int CopiesKept = 7;

    private const string TemporarySuffix = ".tmp";

    /// <summary>Writes today's copy when it is missing, then deletes the oldest dated copies beyond the kept count.</summary>
    /// <returns>The copy written, or null when today's copy was already there.</returns>
    public async Task<string?> WriteTodaysCopyAsync(CancellationToken cancellationToken = default)
    {
        DateOnly today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        string copyFile = Path.Combine(dataPaths.DatabaseCopies, $"jobhunter-{today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.db");
        string? writtenCopy = null;

        if (!File.Exists(copyFile))
        {
            await WriteCopyAsync(copyFile, cancellationToken);
            writtenCopy = copyFile;
        }

        DeleteCopiesBeyondTheKeptCount();

        return writtenCopy;
    }

    /// <summary>Lets SQLite write a consistent snapshot, the newest writes in the write-ahead log included, under a temporary name, and gives it the copy's name only once it is complete, so a sync tool never picks up half a file.</summary>
    /// <remarks>VACUUM INTO writes a compact file in rollback-journal mode, so opening a copy leaves no -wal or -shm beside it; the backup API would carry the source's WAL mode over.</remarks>
    private async Task WriteCopyAsync(string copyFile, CancellationToken cancellationToken)
    {
        string temporaryFile = copyFile + TemporarySuffix;

        try
        {
            File.Delete(temporaryFile);

            await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
            await context.Database.ExecuteSqlAsync($"VACUUM INTO {temporaryFile}", cancellationToken);

            File.Move(temporaryFile, copyFile);
        }
        catch
        {
            File.Delete(temporaryFile);

            throw;
        }
    }

    private void DeleteCopiesBeyondTheKeptCount()
    {
        IEnumerable<string> outdatedCopies = Directory.EnumerateFiles(dataPaths.DatabaseCopies)
            .Where(file => CopyFileName().IsMatch(Path.GetFileName(file)))
            .OrderByDescending(file => Path.GetFileName(file), StringComparer.Ordinal)
            .Skip(CopiesKept);

        foreach (string outdatedCopy in outdatedCopies)
        {
            File.Delete(outdatedCopy);
        }
    }

    [GeneratedRegex("^jobhunter-[0-9]{4}-[0-9]{2}-[0-9]{2}\\.db$")]
    private static partial Regex CopyFileName();
}
