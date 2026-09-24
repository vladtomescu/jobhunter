using JobHunter.Domain;

namespace JobHunter.Tests.Domain;

/// <summary>Covers how a refresh run records its scoring failures and the reason scoring stopped early.</summary>
public sealed class FetchRunTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RecordScoring_WithFailuresDifferingOnlyInTheRequestId_GroupsThemIntoOneReason()
    {
        FetchRun run = FetchRun.Start(FetchTrigger.Startup, Noon);

        run.RecordScoring(0,
        [
            """The request was rejected: Status Code: BadRequest {"type":"error","error":{"type":"invalid_request_error","message":"Bad input."},"request_id":"req_011CabcAAA"}""",
            """The request was rejected: Status Code: BadRequest {"type":"error","error":{"type":"invalid_request_error","message":"Bad input."},"request_id":"req_011CabcBBB"}""",
            """The request was rejected: Status Code: BadRequest   {"type":"error","error":{"type":"invalid_request_error","message":"Bad input."},"request_id":"req_011CabcCCC"}"""
        ]);

        ScoringFailureReason reason = Assert.Single(run.ScoringFailureReasons);
        Assert.Equal(3, reason.Count);
        Assert.Equal(3, run.ScoreFailures);
        Assert.DoesNotContain("req_", reason.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("request_id", reason.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordScoring_WithSeveralReasons_CountsEachAndPutsTheMostFrequentFirst()
    {
        FetchRun run = FetchRun.Start(FetchTrigger.Manual, Noon);

        run.RecordScoring(2, ["Bad schema", "the model refused", "The call timed out: slow", "the model refused", "The call timed out: slow", "the model refused"]);

        Assert.Equal(2, run.Scored);
        Assert.Equal(6, run.ScoreFailures);
        Assert.Equal<ScoringFailureReason>([new("the model refused", 3), new("The call timed out: slow", 2), new("Bad schema", 1)], run.ScoringFailureReasons);
    }

    [Fact]
    public void RecordScoring_WithoutFailures_LeavesNoReasons()
    {
        FetchRun run = FetchRun.Start(FetchTrigger.Manual, Noon);

        run.RecordScoring(5, []);

        Assert.Equal(5, run.Scored);
        Assert.Equal(0, run.ScoreFailures);
        Assert.Empty(run.ScoringFailureReasons);
        Assert.Null(run.ScoringHaltReason);
    }

    [Fact]
    public void HaltScoring_CalledByEveryCallStillInFlight_KeepsTheFirstReasonOnly()
    {
        FetchRun run = FetchRun.Start(FetchTrigger.Startup, Noon);

        run.HaltScoring("Anthropic account usage limit reached — access returns 2026-10-01 00:00 UTC");
        run.HaltScoring("a later reason");

        Assert.Equal("Anthropic account usage limit reached — access returns 2026-10-01 00:00 UTC", run.ScoringHaltReason);
    }

    [Fact]
    public void HaltScoring_WithABlankReason_Throws()
    {
        FetchRun run = FetchRun.Start(FetchTrigger.Startup, Noon);

        Assert.Throws<ArgumentException>(() => run.HaltScoring(" "));
    }
}
