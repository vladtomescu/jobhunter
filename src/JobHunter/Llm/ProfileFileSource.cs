namespace JobHunter.Llm;

/// <summary>Where the catalog reads one profile file from: the user's own copy in the data root, the example shipped with the repository, or nowhere when neither exists.</summary>
public sealed record ProfileFileSource(string FileName, ProfileFileOrigin Origin, string UserPath, string ExamplePath)
{
    /// <summary>The file suffix that marks a shipped example, replacing the plain <c>.md</c> of the user's copy.</summary>
    public const string ExampleSuffix = ".example.md";

    /// <summary>Checks the user's folder first and the repository's examples second, and records which one holds the file.</summary>
    public static ProfileFileSource Resolve(string fileName, string userProfileFolder, string exampleProfileFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(userProfileFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(exampleProfileFolder);

        string userPath = Path.Combine(userProfileFolder, fileName);
        string examplePath = Path.Combine(exampleProfileFolder, Path.GetFileNameWithoutExtension(fileName) + ExampleSuffix);
        ProfileFileOrigin origin = (File.Exists(userPath), File.Exists(examplePath)) switch
        {
            (true, _) => ProfileFileOrigin.DataRoot,
            (false, true) => ProfileFileOrigin.Example,
            _ => ProfileFileOrigin.Missing
        };

        return new ProfileFileSource(fileName, origin, userPath, examplePath);
    }

    /// <summary>The path the file is read from, or null when neither the user's copy nor the example exists.</summary>
    public string? ReadPath => Origin switch
    {
        ProfileFileOrigin.DataRoot => UserPath,
        ProfileFileOrigin.Example => ExamplePath,
        _ => null
    };
}
