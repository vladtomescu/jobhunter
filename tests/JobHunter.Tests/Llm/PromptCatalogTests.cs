using System.Text.Json;
using Anthropic.Models.Messages;
using JobHunter.Llm;

namespace JobHunter.Tests.Llm;

/// <summary>Proves that the prompt material and the two schemas load from the repository and reach the model in the shape the endpoint takes.</summary>
public sealed class PromptCatalogTests
{
    private readonly PromptCatalog catalog = new(LlmFixtures.RepositoryRoot());

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
    public void ScoringSystemPrompt_OfARepositoryWithoutPrompts_SaysWhichFileIsMissing()
    {
        PromptCatalog missing = new(Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N")));

        FileNotFoundException exception = Assert.Throws<FileNotFoundException>(() =>
        {
            _ = missing.ScoringSystemPrompt;
        });

        Assert.Contains("profile.md", exception.Message, StringComparison.Ordinal);
    }
}
