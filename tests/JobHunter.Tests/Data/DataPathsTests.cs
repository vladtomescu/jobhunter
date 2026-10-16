using JobHunter.Data;

namespace JobHunter.Tests.Data;

/// <summary>Proves that the database file sits in the data folder unless a separate database folder is given, and that a separate database folder moves nothing else out of the data folder.</summary>
public sealed class DataPathsTests : IDisposable
{
    private readonly string testFolder = Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void DatabaseFile_WithNoDatabaseFolder_SitsInTheDataFolder()
    {
        string root = Path.Combine(testFolder, "root");

        DataPaths paths = new(root);

        Assert.Equal(Path.Combine(Path.GetFullPath(root), "jobhunter.db"), paths.DatabaseFile);
    }

    [Fact]
    public void DatabaseFile_WithABlankDatabaseFolder_SitsInTheDataFolder()
    {
        string root = Path.Combine(testFolder, "root");

        DataPaths paths = new(root, " ");

        Assert.Equal(Path.Combine(Path.GetFullPath(root), "jobhunter.db"), paths.DatabaseFile);
    }

    [Fact]
    public void DatabaseFile_WithADatabaseFolder_SitsInThatFolderAndCreatesItWhenFirstAskedFor()
    {
        string databaseFolder = Path.Combine(testFolder, "database");
        DataPaths paths = new(Path.Combine(testFolder, "root"), databaseFolder);
        Assert.False(Directory.Exists(databaseFolder));

        string databaseFile = paths.DatabaseFile;

        Assert.Equal(Path.Combine(Path.GetFullPath(databaseFolder), "jobhunter.db"), databaseFile);
        Assert.True(Directory.Exists(databaseFolder));
    }

    [Fact]
    public void Folders_WithADatabaseFolder_StayUnderTheDataFolder()
    {
        string root = Path.GetFullPath(Path.Combine(testFolder, "root"));

        DataPaths paths = new(root, Path.Combine(testFolder, "database"));

        Assert.Equal(root, paths.Root);
        Assert.Equal(Path.Combine(root, "raw"), paths.Raw);
        Assert.Equal(Path.Combine(root, "dataset"), paths.Dataset);
        Assert.Equal(Path.Combine(root, "exchange"), paths.Exchange);
        Assert.Equal(Path.Combine(root, "browser"), paths.Browser);
        Assert.Equal(Path.Combine(root, "fx"), paths.Fx);
        Assert.Equal(Path.Combine(root, "backup"), paths.DatabaseCopies);
    }

    public void Dispose()
    {
        if (Directory.Exists(testFolder))
        {
            Directory.Delete(testFolder, recursive: true);
        }
    }
}
