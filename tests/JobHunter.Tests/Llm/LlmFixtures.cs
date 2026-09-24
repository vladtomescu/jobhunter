using JobHunter.Llm;
using Microsoft.Extensions.Configuration;

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

    /// <summary>A catalog over the repository whose user profile folder does not exist, so every profile file comes from the shipped examples whatever the developer keeps in a data root.</summary>
    public static PromptCatalog ExampleCatalog()
    {
        return new PromptCatalog(RepositoryRoot(), AbsentProfileFolder());
    }

    /// <summary>A profile folder path under the temp folder that nothing creates.</summary>
    public static string AbsentProfileFolder()
    {
        return Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"), "profile");
    }

    /// <summary>A detector over the same sources the application reads: the gitignored local settings file in the source folder and the environment.</summary>
    public static ApiKeyDetector LocalApiKeyDetector()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepositoryRoot(), "src", "JobHunter", ApiKeyDetector.LocalSettingsFile), optional: true)
            .AddEnvironmentVariables()
            .Build();

        return new ApiKeyDetector(configuration);
    }
}
