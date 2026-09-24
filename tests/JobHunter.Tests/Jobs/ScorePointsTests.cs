using JobHunter.Domain;
using JobHunter.Jobs;

namespace JobHunter.Tests.Jobs;

/// <summary>Proves that the score ribbon reads the seven rubric dimensions in the rubric order and lights one pip per earned point.</summary>
public sealed class ScorePointsTests
{
    [Fact]
    public void From_ForAScoreCard_ListsTheSevenDimensionsInRubricOrderWithTheirPoints()
    {
        ScoreCard score = new(0, 1, 2, 0, 1, 2, 1, 7, "Mixed fit.", "senior", "remote", "b2b", null, null, null, null, "European hours.", false, true, "Agent tooling.", [], "claude-opus-5", DateTimeOffset.UnixEpoch, "hash-1");

        IReadOnlyList<ScoreDimension> dimensions = ScorePoints.From(score).Dimensions;

        Assert.Equal<string>(["Niche", "Level", "Stack", "Remote and timezone", "Contract form", "Compensation signal", "Company signal"], [.. dimensions.Select(dimension => dimension.Name)]);
        Assert.Equal<int>([0, 1, 2, 0, 1, 2, 1], [.. dimensions.Select(dimension => dimension.Points)]);
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, true, false)]
    [InlineData(2, true, true)]
    public void Pips_ForADimension_LightsOnePipPerEarnedPointFirstPointFirst(int points, bool firstLit, bool secondLit)
    {
        ScoreDimension dimension = new("Niche", points);

        Assert.Equal<bool>([firstLit, secondLit], dimension.Pips);
    }
}
