using JobHunter.Domain;
using JobHunter.Tests.Jobs;

namespace JobHunter.Tests.Refresh;

/// <summary>Proves which jobs count as waiting for a score, the number the Score button and the Inbox both show.</summary>
public sealed class ScoreBacklogTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CountAsync_WithScoredAndUnscoredJobs_CountsOnlyTheActivePassedUnscoredOnes()
    {
        await using RefreshTestHarness harness = new();
        await harness.InitializeAsync();

        Job scored = ListedJobs.NewInboxJob("Alpha", JobClass.A, 120_000m, Noon);

        Job unscored = ListedJobs.NewJob("Bravo", "Backend Engineer", Noon);
        unscored.ApplyPrefilterVerdict(PrefilterState.Passed, null, []);

        Job droppedUnscored = ListedJobs.NewJob("Charlie", "Backend Engineer", Noon);
        droppedUnscored.ApplyPrefilterVerdict(PrefilterState.Dropped, "title excluded", []);

        await harness.SaveAsync(scored);
        await harness.SaveAsync(unscored);
        await harness.SaveAsync(droppedUnscored);

        Assert.Equal(1, await harness.Backlog.CountAsync());
    }
}
