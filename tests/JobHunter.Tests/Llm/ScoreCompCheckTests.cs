using JobHunter.Llm;
using JobHunter.Llm.Contracts;
using JobHunter.Llm.Exchange;

namespace JobHunter.Tests.Llm;

/// <summary>Proves that the score check refuses pay that cannot be read as it stands and lets readable pay through.</summary>
public sealed class ScoreCompCheckTests
{
    [Fact]
    public void CheckScore_WithAMinUnderOnePercentOfTheMax_ReturnsTheReason()
    {
        string? reason = ExchangeImporter.CheckScore(WithComp(new ScoreCompPayload(90m, 130000m, "EUR", "year")));

        Assert.NotNull(reason);
        Assert.Contains("comp.min is 90", reason, StringComparison.Ordinal);
        Assert.Contains("comp.max 130000", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckScore_WithBothBoundsAsFullNumbers_ReturnsNull()
    {
        Assert.Null(ExchangeImporter.CheckScore(WithComp(new ScoreCompPayload(90000m, 130000m, "EUR", "year"))));
    }

    [Theory]
    [InlineData(90000, 130000)]
    [InlineData(90000, null)]
    [InlineData(null, 130000)]
    public void CheckScore_WithAnAmountAndNoPeriod_ReturnsTheReason(int? min, int? max)
    {
        string? reason = ExchangeImporter.CheckScore(WithComp(new ScoreCompPayload(min, max, "EUR", null)));

        Assert.NotNull(reason);
        Assert.Contains("comp.period is null", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckScore_WithNoAmounts_ReturnsNull()
    {
        Assert.Null(ExchangeImporter.CheckScore(WithComp(new ScoreCompPayload(null, null, null, null))));
    }

    private static ScorePayload WithComp(ScoreCompPayload comp)
    {
        ScorePayload payload = LlmJson.Read<ScorePayload>(LlmFixtures.Read(LlmFixtures.ScorePayloadFile)).Payload!;

        return payload with { Facts = payload.Facts with { Comp = comp } };
    }
}
