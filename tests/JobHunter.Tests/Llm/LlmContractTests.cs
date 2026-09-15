using System.Text.Json;
using System.Text.Json.Nodes;
using JobHunter.Llm;
using JobHunter.Llm.Contracts;

namespace JobHunter.Tests.Llm;

/// <summary>Proves that the payload types and the schema files describe the same objects, in both directions.</summary>
public sealed class LlmContractTests
{
    [Fact]
    public void Read_OfASavedScore_FillsEveryPartOfThePayload()
    {
        ScorePayload payload = LlmJson.Read<ScorePayload>(LlmFixtures.Read(LlmFixtures.ScorePayloadFile)).Payload!;

        Assert.Equal("1f0c2c9a-2f1a-4a3e-9d1a-3b6c8e5d4f21", payload.JobId);
        Assert.Equal(2, payload.Scores.Niche);
        Assert.Equal(2, payload.Scores.RemoteTimezone);
        Assert.Equal("senior", payload.Facts.LevelGuess);
        Assert.Equal(90_000m, payload.Facts.Comp.Min);
        Assert.Equal("year", payload.Facts.Comp.Period);
        Assert.Null(payload.Facts.RequiresUsAuthorization);
        Assert.True(payload.Facts.EndClientNamed);
        Assert.Equal<string>(["b2b"], payload.BlockingUnknowns);
        Assert.NotEmpty(payload.Reasoning);
    }

    [Fact]
    public void ToLine_OfAScoreThatWasRead_WritesTheSamePropertyNamesTheSchemaRequires()
    {
        ScorePayload payload = LlmJson.Read<ScorePayload>(LlmFixtures.Read(LlmFixtures.ScorePayloadFile)).Payload!;

        JsonElement written = JsonDocument.Parse(LlmJson.ToLine(payload)).RootElement;

        Assert.Equal<string>(["job_id", "scores", "facts", "blocking_unknowns", "reasoning"], [.. written.EnumerateObject().Select(property => property.Name)]);
        Assert.Equal<string>(
            ["niche", "level", "stack", "remote_timezone", "contract_form", "comp_signal", "company_signal"],
            [.. written.GetProperty("scores").EnumerateObject().Select(property => property.Name)]);
        Assert.Equal<string>(
            ["level_guess", "remote_policy", "employment_type", "comp", "timezone_note", "requires_us_authorization", "end_client_named", "ai_meaning"],
            [.. written.GetProperty("facts").EnumerateObject().Select(property => property.Name)]);
        Assert.Equal<string>(["min", "max", "currency", "period"], [.. written.GetProperty("facts").GetProperty("comp").EnumerateObject().Select(property => property.Name)]);
    }

    [Fact]
    public void Read_OfASavedKit_FillsEveryPartOfThePayload()
    {
        KitPayload payload = LlmJson.Read<KitPayload>(LlmFixtures.Read(LlmFixtures.KitPayloadFile)).Payload!;

        Assert.Equal("en", payload.Language);
        Assert.Equal(3, payload.FitSummary.Count);
        Assert.NotEmpty(payload.CoverNote);
        Assert.Equal(6, payload.AtsAnswers.Count);
        Assert.Equal("Compensation expectation", payload.AtsAnswers[4].Question);
        Assert.Equal(3, payload.CallQuestions.Count);
    }

    [Fact]
    public void ToLine_OfAKitThatWasRead_WritesTheSamePropertyNamesTheSchemaRequires()
    {
        KitPayload payload = LlmJson.Read<KitPayload>(LlmFixtures.Read(LlmFixtures.KitPayloadFile)).Payload!;

        JsonElement written = JsonDocument.Parse(LlmJson.ToLine(payload)).RootElement;

        Assert.Equal<string>(["job_id", "language", "fit_summary", "cover_note", "ats_answers", "call_questions"], [.. written.EnumerateObject().Select(property => property.Name)]);
        Assert.Equal<string>(["question", "answer"], [.. written.GetProperty("ats_answers")[0].EnumerateObject().Select(property => property.Name)]);
    }

    [Fact]
    public void Read_OfAScoreMissingARequiredProperty_ReportsTheReasonInsteadOfAPayload()
    {
        LlmJsonResult<ScorePayload> result = LlmJson.Read<ScorePayload>(WithoutReasoning());

        Assert.Null(result.Payload);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void Read_OfTextThatIsNotJson_ReportsTheReasonInsteadOfAPayload()
    {
        LlmJsonResult<ScorePayload> result = LlmJson.Read<ScorePayload>("{ not json at all");

        Assert.Null(result.Payload);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    private static string WithoutReasoning()
    {
        JsonNode? node = JsonNode.Parse(LlmFixtures.Read(LlmFixtures.ScorePayloadFile));
        node!.AsObject().Remove("reasoning");

        return node.ToJsonString();
    }
}
