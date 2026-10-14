using JobHunter.Llm.Exchange;

namespace JobHunter.Tests.Llm;

/// <summary>Proves what a claim does: it keeps a job out of other batches for exactly one lease, and a release frees it at once.</summary>
public sealed class ScoreClaimsTests
{
    [Fact]
    public void TryClaim_OnAJobAlreadyClaimed_RefusesUntilTheLeaseRunsOut()
    {
        FakeTimeProvider clock = new(LlmTestJobs.SeenAt);
        ScoreClaims claims = new(clock);
        Guid jobId = Guid.NewGuid();

        bool first = claims.TryClaim(jobId);
        clock.Advance(ScoreClaims.Lease - TimeSpan.FromSeconds(1));
        bool justBeforeExpiry = claims.TryClaim(jobId);
        clock.Advance(TimeSpan.FromSeconds(1));
        bool atExpiry = claims.TryClaim(jobId);

        Assert.Equal(TimeSpan.FromMinutes(30), ScoreClaims.Lease);
        Assert.True(first);
        Assert.False(justBeforeExpiry);
        Assert.True(atExpiry);
    }

    [Fact]
    public void TryClaim_AfterARelease_ClaimsTheJobAgain()
    {
        ScoreClaims claims = new(new FakeTimeProvider(LlmTestJobs.SeenAt));
        Guid jobId = Guid.NewGuid();
        claims.TryClaim(jobId);

        claims.Release(jobId);

        Assert.True(claims.TryClaim(jobId));
    }

    [Fact]
    public void TryClaim_OnAnotherJob_IsNotHeldBackByTheFirstClaim()
    {
        ScoreClaims claims = new(new FakeTimeProvider(LlmTestJobs.SeenAt));
        claims.TryClaim(Guid.NewGuid());

        Assert.True(claims.TryClaim(Guid.NewGuid()));
    }
}
