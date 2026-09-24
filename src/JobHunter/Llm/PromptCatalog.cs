using System.Text.Json;
using JobHunter.Data;

namespace JobHunter.Llm;

/// <summary>Reads the prompt material and the two output schemas that both model paths share, and composes the system block of each call.</summary>
/// <remarks>The prompts and schemas belong to the repository. The profile, the rubric and the question bank belong to the user and are read from the profile folder of the data root, each one falling back on its own to the example the repository ships. Which copy each file comes from is settled when the catalog is built, and each file is read once on first use, so an edited or newly added profile file takes effect after a restart.</remarks>
public sealed class PromptCatalog
{
    /// <summary>The folder name that holds the profile files, both under the data root and in the repository.</summary>
    public const string ProfileFolderName = "profile";

    /// <summary>Who the user is: positioning, constraints, standard answers and voice.</summary>
    public const string ProfileFileName = "profile.md";

    /// <summary>The seven scoring dimensions and their anchors.</summary>
    public const string RubricFileName = "rubric.md";

    /// <summary>The question bank for the first call.</summary>
    public const string QuestionsFileName = "questions.md";

    /// <summary>Schema keywords the structured-output endpoint does not accept, removed before a schema is handed over.</summary>
    public static readonly IReadOnlySet<string> UnsupportedSchemaKeywords = new HashSet<string>(StringComparer.Ordinal) { "$schema", "title" };

    private readonly Lazy<string> profile;
    private readonly Lazy<string> rubric;
    private readonly Lazy<string> questions;
    private readonly Lazy<string> scoreInstructions;
    private readonly Lazy<string> kitInstructions;
    private readonly Lazy<IReadOnlyDictionary<string, JsonElement>> scoreSchema;
    private readonly Lazy<IReadOnlyDictionary<string, JsonElement>> kitSchema;

    /// <summary>Creates the catalog over a repository folder that holds the prompts and the example profile, and the user's own profile folder, which may be absent.</summary>
    public PromptCatalog(string repositoryRootFolder, string userProfileFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRootFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(userProfileFolder);

        string repositoryRoot = Path.GetFullPath(repositoryRootFolder);
        string userFolder = Path.GetFullPath(userProfileFolder);
        string exampleFolder = Path.Combine(repositoryRoot, ProfileFolderName);

        ProfileFileSource profileSource = ProfileFileSource.Resolve(ProfileFileName, userFolder, exampleFolder);
        ProfileFileSource rubricSource = ProfileFileSource.Resolve(RubricFileName, userFolder, exampleFolder);
        ProfileFileSource questionsSource = ProfileFileSource.Resolve(QuestionsFileName, userFolder, exampleFolder);
        ProfileSources = [profileSource, rubricSource, questionsSource];

        profile = new Lazy<string>(() => ReadProfileFile(profileSource));
        rubric = new Lazy<string>(() => ReadProfileFile(rubricSource));
        questions = new Lazy<string>(() => ReadProfileFile(questionsSource));
        scoreInstructions = new Lazy<string>(() => ReadText(Path.Combine(repositoryRoot, "prompts", "score.md")));
        kitInstructions = new Lazy<string>(() => ReadText(Path.Combine(repositoryRoot, "prompts", "kit.md")));
        scoreSchema = new Lazy<IReadOnlyDictionary<string, JsonElement>>(() => ReadSchema(Path.Combine(repositoryRoot, "prompts", "schemas", "score.schema.json")));
        kitSchema = new Lazy<IReadOnlyDictionary<string, JsonElement>>(() => ReadSchema(Path.Combine(repositoryRoot, "prompts", "schemas", "kit.schema.json")));
    }

    /// <summary>Creates the catalog for the repository the data folder belongs to, with the user's profile in the data folder's profile folder.</summary>
    public static PromptCatalog ForDataFolder(DataPaths dataPaths)
    {
        ArgumentNullException.ThrowIfNull(dataPaths);

        string repositoryRoot = Directory.GetParent(dataPaths.Root)?.FullName ?? dataPaths.Root;

        return ForDataFolder(dataPaths, repositoryRoot);
    }

    /// <summary>Creates the catalog over an explicit repository folder, with the user's profile in the data folder's profile folder.</summary>
    public static PromptCatalog ForDataFolder(DataPaths dataPaths, string repositoryRootFolder)
    {
        ArgumentNullException.ThrowIfNull(dataPaths);

        return new PromptCatalog(repositoryRootFolder, Path.Combine(dataPaths.Root, ProfileFolderName));
    }

    /// <summary>Where the profile, the rubric and the question bank are read from, in that order.</summary>
    public IReadOnlyList<ProfileFileSource> ProfileSources { get; }

    /// <summary>The system block of a scoring call: who I am, the rubric and the scoring instructions.</summary>
    public string ScoringSystemPrompt => Join(profile.Value, rubric.Value, scoreInstructions.Value);

    /// <summary>The score schema as the structured-output endpoint takes it, without the keywords it rejects.</summary>
    public IReadOnlyDictionary<string, JsonElement> ScoreSchema => scoreSchema.Value;

    /// <summary>The kit schema as the structured-output endpoint takes it, without the keywords it rejects.</summary>
    public IReadOnlyDictionary<string, JsonElement> KitSchema => kitSchema.Value;

    /// <summary>The system block of a kit call: who I am, the question bank, the resume when it is readable, and the kit instructions.</summary>
    public string KitSystemPrompt(string? resumeMarkdown)
    {
        return string.IsNullOrWhiteSpace(resumeMarkdown)
            ? Join(profile.Value, questions.Value, kitInstructions.Value)
            : Join(profile.Value, questions.Value, $"# Resume{Environment.NewLine}{Environment.NewLine}{resumeMarkdown.Trim()}", kitInstructions.Value);
    }

    private static string Join(params string[] parts)
    {
        return string.Join($"{Environment.NewLine}{Environment.NewLine}", parts);
    }

    private static string ReadProfileFile(ProfileFileSource source)
    {
        if (source.ReadPath is not string path)
        {
            throw new FileNotFoundException($"The profile file {source.FileName} is missing: neither {source.UserPath} nor the example {source.ExamplePath} exists, so the model cannot be given its instructions.", source.UserPath);
        }

        return ReadText(path);
    }

    private static string ReadText(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"The prompt file {path} is missing, so the model cannot be given its instructions.", path);
        }

        return File.ReadAllText(path).Trim();
    }

    private static IReadOnlyDictionary<string, JsonElement> ReadSchema(string path)
    {
        using JsonDocument document = JsonDocument.Parse(ReadText(path));
        Dictionary<string, JsonElement> schema = [];

        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            if (!UnsupportedSchemaKeywords.Contains(property.Name))
            {
                schema[property.Name] = property.Value.Clone();
            }
        }

        return schema;
    }
}
