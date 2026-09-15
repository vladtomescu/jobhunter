using System.Text.Json;
using JobHunter.Data;

namespace JobHunter.Llm;

/// <summary>Reads the prompt material and the two output schemas that both model paths share, and composes the system block of each call.</summary>
/// <remarks>The files live in the repository next to the data folder, so the catalog resolves them from the data folder rather than from a fixed path.</remarks>
public sealed class PromptCatalog
{
    /// <summary>Schema keywords the structured-output endpoint does not accept, removed before a schema is handed over.</summary>
    public static readonly IReadOnlySet<string> UnsupportedSchemaKeywords = new HashSet<string>(StringComparer.Ordinal) { "$schema", "title" };

    private readonly string repositoryRoot;
    private readonly Lazy<string> profile;
    private readonly Lazy<string> rubric;
    private readonly Lazy<string> questions;
    private readonly Lazy<string> scoreInstructions;
    private readonly Lazy<string> kitInstructions;
    private readonly Lazy<IReadOnlyDictionary<string, JsonElement>> scoreSchema;
    private readonly Lazy<IReadOnlyDictionary<string, JsonElement>> kitSchema;

    /// <summary>Creates the catalog over a repository folder that holds the prompts and profile folders.</summary>
    public PromptCatalog(string repositoryRootFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRootFolder);

        repositoryRoot = Path.GetFullPath(repositoryRootFolder);
        profile = new Lazy<string>(() => ReadText(Path.Combine(repositoryRoot, "profile", "profile.md")));
        rubric = new Lazy<string>(() => ReadText(Path.Combine(repositoryRoot, "profile", "rubric.md")));
        questions = new Lazy<string>(() => ReadText(Path.Combine(repositoryRoot, "profile", "questions.md")));
        scoreInstructions = new Lazy<string>(() => ReadText(Path.Combine(repositoryRoot, "prompts", "score.md")));
        kitInstructions = new Lazy<string>(() => ReadText(Path.Combine(repositoryRoot, "prompts", "kit.md")));
        scoreSchema = new Lazy<IReadOnlyDictionary<string, JsonElement>>(() => ReadSchema(Path.Combine(repositoryRoot, "prompts", "schemas", "score.schema.json")));
        kitSchema = new Lazy<IReadOnlyDictionary<string, JsonElement>>(() => ReadSchema(Path.Combine(repositoryRoot, "prompts", "schemas", "kit.schema.json")));
    }

    /// <summary>Creates the catalog for the repository the data folder belongs to.</summary>
    public static PromptCatalog ForDataFolder(DataPaths dataPaths)
    {
        ArgumentNullException.ThrowIfNull(dataPaths);

        string root = Directory.GetParent(dataPaths.Root)?.FullName ?? dataPaths.Root;

        return new PromptCatalog(root);
    }

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
