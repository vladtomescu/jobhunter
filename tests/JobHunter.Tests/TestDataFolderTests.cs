using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

namespace JobHunter.Tests;

public sealed class TestDataFolderTests
{
    private const int OtherTests = 8;

    private const int QueriesPerOtherTest = 300;

    /// <summary>Stands in for the test classes xunit runs side by side: each keeps opening its own database while this one deletes its folder over and over.</summary>
    /// <remarks>Clearing every pool disposed a connection another database was taking from its pool at that instant; the interleaving cannot be forced from outside the library, so the test repeats it until the other databases are done, which failed within the first few hundred queries.</remarks>
    [Fact]
    public async Task Delete_WhileOtherTestsQueryTheirOwnDatabases_BreaksNoneOfTheirConnections()
    {
        string ownFolder = NewFolderPath();
        string[] otherFolders = [.. Enumerable.Range(0, OtherTests).Select(_ => Directory.CreateDirectory(NewFolderPath()).FullName)];
        ConcurrentQueue<Exception> failures = new();
        int otherTestsRunning = OtherTests;

        async Task QueryOwnDatabaseAsync(string folder)
        {
            try
            {
                for (int query = 0; query < QueriesPerOtherTest; query++)
                {
                    await using SqliteConnection connection = new(TestDataFolder.ConnectionString(folder));
                    await connection.OpenAsync();
                    await using SqliteCommand command = connection.CreateCommand();
                    command.CommandText = "SELECT 1";
                    await command.ExecuteScalarAsync();
                }
            }
            catch (Exception exception)
            {
                failures.Enqueue(exception);
            }
            finally
            {
                Interlocked.Decrement(ref otherTestsRunning);
            }
        }

        Task[] otherTests = [.. otherFolders.Select(folder => Task.Run(() => QueryOwnDatabaseAsync(folder)))];

        while (Volatile.Read(ref otherTestsRunning) > 0)
        {
            TestDataFolder.Delete(ownFolder);
        }

        await Task.WhenAll(otherTests);

        foreach (string folder in otherFolders)
        {
            TestDataFolder.Delete(folder);
        }

        Assert.Empty(failures);
    }

    private static string NewFolderPath()
    {
        return Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"));
    }
}
