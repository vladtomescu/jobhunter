using JobHunter.Llm;
using JobHunter.Llm.Contracts;

namespace JobHunter.Tests.Llm;

/// <summary>A fact that reaches the paid model, so it runs only when live tests are switched on and a key is present.</summary>
public sealed class AnthropicLiveFactAttribute : FactAttribute
{
    /// <summary>Skips the test unless live tests are switched on and a key is configured.</summary>
    public AnthropicLiveFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(LiveFactAttribute.SwitchName), "1", StringComparison.Ordinal))
        {
            Skip = $"Set {LiveFactAttribute.SwitchName}=1 to run tests that reach the network or a paid API.";
        }
        else if (LlmFixtures.LocalApiKeyDetector().Read() is null)
        {
            Skip = $"No Anthropic key in {ApiKeyDetector.LocalSettingsFile} or {ApiKeyDetector.VariableName}, so the model cannot be reached.";
        }
    }
}

/// <summary>Sends one saved posting to the model and checks that what comes back fits the score schema.</summary>
public sealed class AnthropicLiveTests
{
    [AnthropicLiveFact]
    public async Task ScoreAsync_AgainstTheModel_ReturnsAScoreThatFitsTheSchema()
    {
        ApiKeyDetector detector = LlmFixtures.LocalApiKeyDetector();
        AnthropicJobScorer scorer = new(new AnthropicClientFactory(detector), new PromptCatalog(LlmFixtures.RepositoryRoot()), detector);
        ScoreRequest request = new(
            Guid.CreateVersion7(),
            "Senior Backend Engineer, Developer Platform",
            "Northwind Labs",
            "Remote, Europe",
            "remote",
            null,
            "85000-110000 EUR per year",
            DateTimeOffset.UtcNow.AddDays(-2),
            ["H1"],
            LlmFixtures.Read(LlmFixtures.LivePostingFile),
            "claude-opus-5");

        ScoreOutcome outcome = await scorer.ScoreAsync(request, CancellationToken.None);

        Assert.True(outcome.IsSuccess, outcome.FailureReason);
        ScorePayload payload = outcome.Payload!;
        Assert.Equal(request.JobId.ToString(), payload.JobId);
        Assert.All(
            new[] { payload.Scores.Niche, payload.Scores.Level, payload.Scores.Stack, payload.Scores.RemoteTimezone, payload.Scores.ContractForm, payload.Scores.CompSignal, payload.Scores.CompanySignal },
            score => Assert.InRange(score, 0, 2));
        Assert.Contains(payload.Facts.LevelGuess, new[] { "junior", "mid", "senior", "staff", "principal", "lead", "unknown" });
        Assert.Contains(payload.Facts.RemotePolicy, new[] { "remote", "hybrid", "onsite", "unknown" });
        Assert.Contains(payload.Facts.EmploymentType, new[] { "b2b", "employment", "either", "unknown" });
        Assert.All(payload.BlockingUnknowns, unknown => Assert.Contains(unknown, new[] { "end_client", "b2b", "timezone", "us_authorization" }));
        Assert.NotEmpty(payload.Reasoning);
        Assert.Equal("claude-opus-5", outcome.Model);
        Assert.True(outcome.Usage!.InputTokens > 0);
    }
}
