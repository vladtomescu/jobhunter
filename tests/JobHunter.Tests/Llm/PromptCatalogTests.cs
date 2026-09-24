using System.Text.Json;
using System.Text.RegularExpressions;
using Anthropic.Models.Messages;
using JobHunter.Llm;

namespace JobHunter.Tests.Llm;

/// <summary>Proves that the prompt material and the two schemas load from the repository, that each profile file comes from the data root before the shipped example, and that the composed prompts reach the model in the shape the endpoint takes.</summary>
public sealed partial class PromptCatalogTests : IDisposable
{
    private readonly PromptCatalog catalog = LlmFixtures.ExampleCatalog();
    private readonly string testFolder = Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(testFolder))
        {
            Directory.Delete(testFolder, recursive: true);
        }
    }

    [Fact]
    public void ProfileSources_WithNoProfileInTheDataRoot_NameTheShippedExamples()
    {
        Assert.Equal<string>([PromptCatalog.ProfileFileName, PromptCatalog.RubricFileName, PromptCatalog.QuestionsFileName], [.. catalog.ProfileSources.Select(source => source.FileName)]);
        Assert.All(catalog.ProfileSources, source =>
        {
            Assert.Equal(ProfileFileOrigin.Example, source.Origin);
            Assert.EndsWith(ProfileFileSource.ExampleSuffix, source.ReadPath, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ScoringSystemPrompt_WithEveryProfileFileInTheDataRoot_ReadsTheDataRootCopiesInsteadOfTheExamples()
    {
        string userFolder = WriteUserProfile(
            (PromptCatalog.ProfileFileName, "# Profile\n\nData-root profile marker."),
            (PromptCatalog.RubricFileName, "# Rubric\n\nData-root rubric marker."),
            (PromptCatalog.QuestionsFileName, "# Questions for the first call\n\nData-root questions marker."));
        PromptCatalog userCatalog = new(LlmFixtures.RepositoryRoot(), userFolder);

        string scoring = userCatalog.ScoringSystemPrompt;
        string kit = userCatalog.KitSystemPrompt(null);

        Assert.All(userCatalog.ProfileSources, source => Assert.Equal(ProfileFileOrigin.DataRoot, source.Origin));
        Assert.Contains("Data-root profile marker.", scoring, StringComparison.Ordinal);
        Assert.Contains("Data-root rubric marker.", scoring, StringComparison.Ordinal);
        Assert.Contains("Data-root questions marker.", kit, StringComparison.Ordinal);
        Assert.DoesNotContain("Exampleland", scoring + kit, StringComparison.Ordinal);
        Assert.Contains("# Scoring instructions", scoring, StringComparison.Ordinal);
    }

    [Fact]
    public void ScoringSystemPrompt_WithOnlyTheProfileInTheDataRoot_FallsBackToTheExampleRubricForThatFileAlone()
    {
        string userFolder = WriteUserProfile((PromptCatalog.ProfileFileName, "# Profile\n\nData-root profile marker."));
        PromptCatalog userCatalog = new(LlmFixtures.RepositoryRoot(), userFolder);

        string scoring = userCatalog.ScoringSystemPrompt;

        Assert.Equal<ProfileFileOrigin>([ProfileFileOrigin.DataRoot, ProfileFileOrigin.Example, ProfileFileOrigin.Example], [.. userCatalog.ProfileSources.Select(source => source.Origin)]);
        Assert.Contains("Data-root profile marker.", scoring, StringComparison.Ordinal);
        Assert.Contains("This is the example rubric", scoring, StringComparison.Ordinal);
        Assert.DoesNotContain("This is the example profile", scoring, StringComparison.Ordinal);
    }

    [Fact]
    public void ScoringSystemPrompt_WithNeitherTheDataRootCopyNorTheExample_NamesTheFileAndBothPlacesItLooked()
    {
        string repositoryRoot = Path.Combine(testFolder, "repository");
        string userFolder = Path.Combine(testFolder, "data", "profile");
        PromptCatalog missing = new(repositoryRoot, userFolder);

        FileNotFoundException exception = Assert.Throws<FileNotFoundException>(() =>
        {
            _ = missing.ScoringSystemPrompt;
        });

        Assert.Equal(ProfileFileOrigin.Missing, missing.ProfileSources[0].Origin);
        Assert.Contains(PromptCatalog.ProfileFileName, exception.Message, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(userFolder, PromptCatalog.ProfileFileName), exception.Message, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(repositoryRoot, PromptCatalog.ProfileFolderName, "profile.example.md"), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ScoringSystemPrompt_FromTheShippedExamples_CarriesARubricSectionForEveryDimensionTheSchemaScores()
    {
        string prompt = catalog.ScoringSystemPrompt;

        foreach (JsonProperty dimension in catalog.ScoreSchema["properties"].GetProperty("scores").GetProperty("properties").EnumerateObject())
        {
            Assert.Matches($@"(?m)^## \d\. {Regex.Escape(dimension.Name)}\b", prompt);
        }
    }

    [Fact]
    public void KitSystemPrompt_FromTheShippedExamples_CarriesEveryProfileSectionTheInstructionsReferTo()
    {
        string prompt = catalog.KitSystemPrompt(null) + Environment.NewLine + catalog.ScoringSystemPrompt;
        string[] referencedSections = [.. ProfileSectionReference().Matches(prompt).Select(match => match.Groups["section"].Value).Distinct(StringComparer.Ordinal)];

        Assert.Contains("Voice", referencedSections);
        Assert.Contains("Standard answers", referencedSections);
        Assert.All(referencedSections, section => Assert.Matches($@"(?m)^## {Regex.Escape(section)}\s*$", prompt));
    }

    [Fact]
    public void ScoreSchema_LoadedFromTheRepository_LeavesOutTheKeywordsTheEndpointRejects()
    {
        IReadOnlyDictionary<string, JsonElement> schema = catalog.ScoreSchema;

        Assert.DoesNotContain("$schema", schema.Keys);
        Assert.DoesNotContain("title", schema.Keys);
        Assert.Contains("type", schema.Keys);
        Assert.Contains("properties", schema.Keys);
        Assert.Contains("required", schema.Keys);
    }

    [Fact]
    public void ScoreSchema_LoadedFromTheRepository_CarriesTheSevenDimensionsAndTheFacts()
    {
        JsonElement properties = catalog.ScoreSchema["properties"];

        Assert.True(properties.TryGetProperty("scores", out JsonElement scores));
        Assert.True(properties.TryGetProperty("facts", out _));
        Assert.Equal(7, scores.GetProperty("properties").EnumerateObject().Count());
    }

    [Fact]
    public void KitSchema_LoadedFromTheRepository_LeavesOutTheKeywordsTheEndpointRejects()
    {
        IReadOnlyDictionary<string, JsonElement> schema = catalog.KitSchema;

        Assert.DoesNotContain("$schema", schema.Keys);
        Assert.DoesNotContain("title", schema.Keys);
        Assert.Contains("properties", schema.Keys);
        Assert.Contains("cover_note", schema["properties"].EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public void ScoreSchema_HandedToTheOutputFormat_FillsTheSchemaTheEndpointReads()
    {
        JsonOutputFormat format = new() { Schema = catalog.ScoreSchema };

        Assert.Equal(catalog.ScoreSchema.Count, format.Schema.Count);
        Assert.Equal(JsonValueKind.Object, format.Schema["properties"].ValueKind);
    }

    [Fact]
    public void KitSchema_HandedToTheOutputFormat_FillsTheSchemaTheEndpointReads()
    {
        JsonOutputFormat format = new() { Schema = catalog.KitSchema };

        Assert.Equal(catalog.KitSchema.Count, format.Schema.Count);
    }

    [Fact]
    public void ScoringSystemPrompt_Composed_CarriesTheProfileTheRubricAndTheInstructions()
    {
        string prompt = catalog.ScoringSystemPrompt;

        Assert.Contains("# Profile", prompt, StringComparison.Ordinal);
        Assert.Contains("# Rubric", prompt, StringComparison.Ordinal);
        Assert.Contains("# Scoring instructions", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void KitSystemPrompt_WithAResume_CarriesTheProfileTheQuestionsTheResumeAndTheInstructions()
    {
        string prompt = catalog.KitSystemPrompt("My resume, in markdown.");

        Assert.Contains("# Profile", prompt, StringComparison.Ordinal);
        Assert.Contains("# Questions for the first call", prompt, StringComparison.Ordinal);
        Assert.Contains("My resume, in markdown.", prompt, StringComparison.Ordinal);
        Assert.Contains("# Kit instructions", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void KitSystemPrompt_WithoutAResume_StillCarriesTheInstructions()
    {
        string prompt = catalog.KitSystemPrompt(null);

        Assert.Contains("# Kit instructions", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("# Resume", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ScoringSystemPrompt_OfARepositoryWithoutPrompts_SaysWhichPromptFileIsMissing()
    {
        string userFolder = WriteUserProfile(
            (PromptCatalog.ProfileFileName, "# Profile"),
            (PromptCatalog.RubricFileName, "# Rubric"),
            (PromptCatalog.QuestionsFileName, "# Questions for the first call"));
        PromptCatalog missing = new(Path.Combine(testFolder, "repository"), userFolder);

        FileNotFoundException exception = Assert.Throws<FileNotFoundException>(() =>
        {
            _ = missing.ScoringSystemPrompt;
        });

        Assert.Contains("score.md", exception.Message, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"profile's (?<section>[A-Z][a-z]+(?: [a-z]+)*) section")]
    private static partial Regex ProfileSectionReference();

    private string WriteUserProfile(params (string FileName, string Content)[] files)
    {
        string userFolder = Path.Combine(testFolder, "data", PromptCatalog.ProfileFolderName);
        Directory.CreateDirectory(userFolder);

        foreach ((string fileName, string content) in files)
        {
            File.WriteAllText(Path.Combine(userFolder, fileName), content);
        }

        return userFolder;
    }
}
