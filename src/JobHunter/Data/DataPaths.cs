namespace JobHunter.Data;

/// <summary>The folders under the repository-level data folder; every folder is created the first time it is asked for.</summary>
public sealed class DataPaths
{
    private readonly string rootFolder;

    /// <summary>Creates the helper over an explicit data folder, which is what tests use.</summary>
    public DataPaths(string rootFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootFolder);

        this.rootFolder = Path.GetFullPath(rootFolder);
    }

    /// <summary>Creates the helper over the data folder next to the solution, two levels above the content root.</summary>
    public static DataPaths FromContentRoot(string contentRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

        return new DataPaths(Path.Combine(contentRootPath, "..", "..", "data"));
    }

    /// <summary>The data folder itself.</summary>
    public string Root => Ensure(rootFolder);

    /// <summary>Raw source responses, one folder per source and day.</summary>
    public string Raw => Ensure(Path.Combine(rootFolder, "raw"));

    /// <summary>Downloaded dataset slices and their manifest.</summary>
    public string Dataset => Ensure(Path.Combine(rootFolder, "dataset"));

    /// <summary>The export and import files of the backup path that runs without an API key.</summary>
    public string Exchange => Ensure(Path.Combine(rootFolder, "exchange"));

    /// <summary>The persistent browser profile used for form prefill.</summary>
    public string Browser => Ensure(Path.Combine(rootFolder, "browser"));

    /// <summary>Cached exchange rates.</summary>
    public string Fx => Ensure(Path.Combine(rootFolder, "fx"));

    /// <summary>The SQLite database file.</summary>
    public string DatabaseFile => Path.Combine(Root, "jobhunter.db");

    private static string Ensure(string folder)
    {
        Directory.CreateDirectory(folder);

        return folder;
    }
}
