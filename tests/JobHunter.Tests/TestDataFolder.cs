using JobHunter.Data;
using Microsoft.Data.Sqlite;

namespace JobHunter.Tests;

/// <summary>Removes the temp data folder a test kept its SQLite database in, once the test has let go of the database.</summary>
internal static class TestDataFolder
{
    /// <summary>The connection string the application opens the folder's database with, which is also the key of that database's pool.</summary>
    public static string ConnectionString(string dataFolder)
    {
        return $"Data Source={new DataPaths(dataFolder).DatabaseFile}";
    }

    /// <summary>Clears the pool of the folder's own database, whose idle connections hold the file open, then deletes the folder.</summary>
    /// <remarks>Never SqliteConnection.ClearAllPools: xunit runs test classes side by side, and clearing every pool disposes a connection another test is taking from its own pool at that instant, which surfaces there as an ObjectDisposedException or, inside a run that records its own failure, as a wrong count.</remarks>
    public static void Delete(string dataFolder)
    {
        using SqliteConnection connection = new(ConnectionString(dataFolder));
        SqliteConnection.ClearPool(connection);

        if (Directory.Exists(dataFolder))
        {
            Directory.Delete(dataFolder, recursive: true);
        }
    }
}
