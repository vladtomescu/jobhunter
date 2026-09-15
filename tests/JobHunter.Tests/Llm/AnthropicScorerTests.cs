using JobHunter.Llm;
using JobHunter.Llm.Contracts;

namespace JobHunter.Tests.Llm;

/// <summary>A detector that reports no key, so the tests can prove what happens when the environment holds none.</summary>
internal sealed class AbsentApiKeyDetector : ApiKeyDetector
{
    /// <inheritdoc />
    public override string? Read()
    {
        return null;
    }
}

/// <summary>Proves that a missing key and a failed call come back as outcomes rather than exceptions.</summary>
public sealed class AnthropicScorerTests
{
    private static readonly ScoreRequest Request = new(
        Guid.CreateVersion7(),
        "Senior Backend Engineer",
        "Northwind Labs",
        "Remote, Europe",
        "remote",
        "b2b",
        "90000-120000 EUR per year",
        new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero),
        ["H1"],
        "Plain text description.",
        "claude-opus-5");

    [Fact]
    public async Task ScoreAsync_WithoutAKeyInTheEnvironment_ReturnsAFailureThatNamesTheVariable()
    {
        AbsentApiKeyDetector detector = new();
        AnthropicJobScorer scorer = new(new AnthropicClientFactory(detector), new PromptCatalog(LlmFixtures.RepositoryRoot()), detector);

        ScoreOutcome outcome = await scorer.ScoreAsync(Request, CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.False(outcome.Retryable);
        Assert.Contains(ApiKeyDetector.VariableName, outcome.FailureReason ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_WithoutAKeyInTheEnvironment_ReturnsAFailureThatNamesTheVariable()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        AbsentApiKeyDetector detector = new();
        AnthropicKitWriter writer = new(new AnthropicClientFactory(detector), new PromptCatalog(LlmFixtures.RepositoryRoot()), detector, harness.Settings);

        KitOutcome outcome = await writer.WriteAsync(NewKitRequest(), CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.False(outcome.Retryable);
        Assert.Contains(ApiKeyDetector.VariableName, outcome.FailureReason ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_WithoutAKeyInTheEnvironment_RefusesToBuildAClient()
    {
        AnthropicClientFactory factory = new(new AbsentApiKeyDetector());

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = factory.Create();
        });

        Assert.Contains(ApiKeyDetector.VariableName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_OfATimeout_SaysTheCallIsWorthRepeating()
    {
        LlmFailureDescription failure = AnthropicFailure.Describe(new TaskCanceledException("The request timed out."));

        Assert.True(failure.Retryable);
        Assert.Contains("timed out", failure.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Describe_OfAnErrorFromNowhereKnown_SaysTheCallIsNotWorthRepeating()
    {
        LlmFailureDescription failure = AnthropicFailure.Describe(new InvalidOperationException("Something else went wrong."));

        Assert.False(failure.Retryable);
        Assert.Contains("Something else went wrong.", failure.Reason, StringComparison.Ordinal);
    }

    private static KitRequest NewKitRequest()
    {
        ScorePayload score = LlmJson.Read<ScorePayload>(LlmFixtures.Read(LlmFixtures.ScorePayloadFile)).Payload!;

        return new KitRequest(
            Guid.CreateVersion7(),
            "Senior Backend Engineer",
            "Northwind Labs",
            "https://jobs.example.com/apply",
            "Plain text description.",
            score,
            "A",
            ["H1"],
            "en",
            "claude-opus-5");
    }
}
