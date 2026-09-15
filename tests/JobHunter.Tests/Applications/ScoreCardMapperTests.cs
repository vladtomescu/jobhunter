using JobHunter.Applications;
using JobHunter.Domain;
using JobHunter.Llm.Contracts;

namespace JobHunter.Tests.Applications;

/// <summary>Covers the score card to score payload mapping, which the kit request depends on to carry the full rubric result.</summary>
public sealed class ScoreCardMapperTests
{
    [Fact]
    public void ToScorePayload_ForAFullScoreCard_MapsEveryDimensionFactAndBlockingUnknown()
    {
        Guid jobId = Guid.CreateVersion7();
        ScoreCard score = new(
            2, 1, 2, 1, 0, 2, 1, 9, "Strong niche fit, timezone unclear.", "senior", "remote", "b2b",
            90_000m, 120_000m, "USD", CompPeriod.Year, "Overlaps five European hours.",
            false, true, "Builds the agent harness itself.", ["timezone", "end_client"],
            "claude-opus-5", new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero), "hash-1");

        ScorePayload payload = ScoreCardMapper.ToScorePayload(jobId, score);

        Assert.Equal(jobId.ToString(), payload.JobId);
        Assert.Equal(2, payload.Scores.Niche);
        Assert.Equal(1, payload.Scores.Level);
        Assert.Equal(2, payload.Scores.Stack);
        Assert.Equal(1, payload.Scores.RemoteTimezone);
        Assert.Equal(0, payload.Scores.ContractForm);
        Assert.Equal(2, payload.Scores.CompSignal);
        Assert.Equal(1, payload.Scores.CompanySignal);
        Assert.Equal("senior", payload.Facts.LevelGuess);
        Assert.Equal("remote", payload.Facts.RemotePolicy);
        Assert.Equal("b2b", payload.Facts.EmploymentType);
        Assert.Equal(90_000m, payload.Facts.Comp.Min);
        Assert.Equal(120_000m, payload.Facts.Comp.Max);
        Assert.Equal("USD", payload.Facts.Comp.Currency);
        Assert.Equal("year", payload.Facts.Comp.Period);
        Assert.False(payload.Facts.RequiresUsAuthorization);
        Assert.True(payload.Facts.EndClientNamed);
        Assert.Equal<string>(["timezone", "end_client"], payload.BlockingUnknowns);
        Assert.Equal("Strong niche fit, timezone unclear.", payload.Reasoning);
    }

    [Fact]
    public void ToScorePayload_WhenCompPeriodIsNull_LeavesThePeriodNull()
    {
        ScoreCard score = new(
            1, 1, 1, 1, 1, 1, 1, 7, "Reasoning.", "mid", "hybrid", "employment",
            null, null, null, null, "No timezone note.", null, null, "Unclear.", [],
            "claude-opus-5", new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero), "hash-1");

        ScorePayload payload = ScoreCardMapper.ToScorePayload(Guid.CreateVersion7(), score);

        Assert.Null(payload.Facts.Comp.Period);
        Assert.Empty(payload.BlockingUnknowns);
    }
}
