namespace JobHunter.Tests.Llm;

/// <summary>Reads the saved payloads and postings the language model tests work from.</summary>
internal static class LlmFixtures
{
    /// <summary>A score exactly as the score schema defines it, on one line.</summary>
    public const string ScorePayloadFile = "llm-score-payload.json";

    /// <summary>A kit exactly as the kit schema defines it, on one line, written so that the lint finds nothing.</summary>
    public const string KitPayloadFile = "llm-kit-payload.json";

    /// <summary>A posting the live test sends to the model.</summary>
    public const string LivePostingFile = "llm-live-posting.txt";

    /// <summary>Reads one fixture from the folder the test project copies next to its assembly.</summary>
    public static string Read(string name)
    {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name)).Trim();
    }

    /// <summary>The repository folder that holds the prompts and profile folders, found by walking up from the test assembly.</summary>
    public static string RepositoryRoot()
    {
        DirectoryInfo? folder = new(AppContext.BaseDirectory);

        while (folder is not null)
        {
            if (File.Exists(Path.Combine(folder.FullName, "prompts", "schemas", "score.schema.json")))
            {
                return folder.FullName;
            }

            folder = folder.Parent;
        }

        throw new DirectoryNotFoundException("No folder above the test assembly holds the prompts folder.");
    }
}
