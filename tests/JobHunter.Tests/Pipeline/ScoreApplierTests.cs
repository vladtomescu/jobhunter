using JobHunter.Domain;
using JobHunter.Llm.Contracts;
using JobHunter.Pipeline;
using JobHunter.Sources;

namespace JobHunter.Tests.Pipeline;

/// <summary>Proves the one place a score becomes a stored job: compensation normalized, signal recomputed, class decided, score card written.</summary>
public sealed class ScoreApplierTests
{
    private static readonly DateTimeOffset ScoredAt = new(2026, 9, 14, 18, 0, 0, TimeSpan.Zero);

    private readonly ScoreApplier applier = new(new CompNormalizer(new FakeFxRateProvider()));

    [Fact]
    public async Task ApplyAsync_ForAJobWhoseSourceStatedComp_NormalizesThatCompToEuroPerYear()
    {
        Job job = NewJob();
        job.RecordCompensation(100_000m, 150_000m, "USD", CompPeriod.Year, null, null);

        await applier.ApplyAsync(job, Payload(), "claude-opus-5", Settings(minB2bHourly: 45m, target: 120_000m), ScoredAt);

        Assert.Equal(80_000m, job.CompMinPerYear);
        Assert.Equal(120_000m, job.CompMaxPerYear);
    }

    [Fact]
    public async Task ApplyAsync_ForAJobWhoseCompOnlyTheModelFound_RecordsThatCompOnTheJob()
    {
        Job job = NewJob();

        await applier.ApplyAsync(job, Payload(compMin: 60m, compMax: 70m, currency: "EUR", period: "hour"), "claude-opus-5", Settings(), ScoredAt);

        Assert.Equal(60m, job.CompMin);
        Assert.Equal(CompPeriod.Hour, job.CompPeriod);
        Assert.Equal(105_600m, job.CompMinPerYear);
        Assert.Equal(123_200m, job.CompMaxPerYear);
    }

    [Fact]
    public async Task ApplyAsync_ForAJobWhoseCompOnlyTheModelFound_DropsTheCompensationUnknownFlag()
    {
        Job job = NewJob();
        job.ApplyPrefilterVerdict(PrefilterState.Passed, null, [JobFlag.H1, JobFlag.CU]);

        await applier.ApplyAsync(job, Payload(compMin: 40m, compMax: 45m, currency: "EUR", period: "hour"), "claude-opus-5", Settings(), ScoredAt);

        Assert.Equal<JobFlag>([JobFlag.H1], job.Flags);
    }

    [Fact]
    public async Task ApplyAsync_ForAJobWhosePayOnlyTheModelFoundReachesTheThreshold_RaisesTheHighPayFlag()
    {
        Job job = NewJob();

        await applier.ApplyAsync(job, Payload(compMin: 60m, compMax: 70m, currency: "EUR", period: "hour"), "claude-opus-5", Settings(), ScoredAt);

        Assert.Contains(JobFlag.HighPay, job.Flags);
    }

    [Fact]
    public async Task ApplyAsync_ForAJobWhosePayNobodyFound_LeavesTheHighPayFlagOff()
    {
        Job job = NewJob();

        await applier.ApplyAsync(job, Payload(), "claude-opus-5", Settings(), ScoredAt);

        Assert.DoesNotContain(JobFlag.HighPay, job.Flags);
    }

    [Fact]
    public async Task ApplyAsync_ForAJobWhoseCompNobodyFound_KeepsTheCompensationUnknownFlag()
    {
        Job job = NewJob();
        job.ApplyPrefilterVerdict(PrefilterState.Passed, null, [JobFlag.H1, JobFlag.CU]);

        await applier.ApplyAsync(job, Payload(), "claude-opus-5", Settings(), ScoredAt);

        Assert.Equal<JobFlag>([JobFlag.H1, JobFlag.CU], job.Flags);
    }

    [Fact]
    public async Task ApplyAsync_ForAScoredJob_StoresTheScoreCardWithTheRecomputedSignalAndTotal()
    {
        Job job = NewJob();
        job.RecordCompensation(140_000m, 160_000m, "EUR", CompPeriod.Year, null, null);

        Classification classification = await applier.ApplyAsync(
            job,
            Payload(niche: 2, level: 2, stack: 2, remoteTimezone: 1, contractForm: 1, compSignal: 0, companySignal: 1, blockingUnknowns: []),
            "claude-opus-5",
            Settings(minB2bHourly: 45m, target: 120_000m),
            ScoredAt);

        Assert.NotNull(job.Score);
        Assert.Equal(2, job.Score.CompSignal);
        Assert.Equal(11, job.Score.Total);
        Assert.Equal(11, classification.Total);
        Assert.Equal(JobClass.A, job.Class);
        Assert.Equal(ScoringState.Scored, job.Scoring);
    }

    [Fact]
    public async Task ApplyAsync_ForAScoredJob_RecordsTheModelTheMomentAndTheDescriptionItScored()
    {
        Job job = NewJob();

        await applier.ApplyAsync(job, Payload(), "claude-code", Settings(), ScoredAt);

        Assert.NotNull(job.Score);
        Assert.Equal("claude-code", job.Score.Model);
        Assert.Equal(ScoredAt, job.Score.ScoredAt);
        Assert.Equal(job.DescriptionHash, job.Score.DescriptionHashAtScoring);
    }

    [Fact]
    public async Task ApplyAsync_ForAScoredJob_KeepsTheFactsAndReasoningTheModelReturned()
    {
        Job job = NewJob();

        await applier.ApplyAsync(job, Payload(), "claude-opus-5", Settings(), ScoredAt);

        Assert.NotNull(job.Score);
        Assert.Equal("staff", job.Score.LevelGuess);
        Assert.Equal("remote", job.Score.RemotePolicy);
        Assert.Equal("Platform work in the niche.", job.Score.Reasoning);
        Assert.Equal<string>(["timezone"], job.Score.BlockingUnknowns);
    }

    [Fact]
    public async Task ApplyAsync_ForAJobPayingBelowTheMinimum_ReturnsClassC()
    {
        Job job = NewJob();
        job.RecordCompensation(40_000m, 50_000m, "EUR", CompPeriod.Year, null, null);

        Classification classification = await applier.ApplyAsync(
            job,
            Payload(niche: 2, level: 2, stack: 2, remoteTimezone: 2, contractForm: 2, compSignal: 2, companySignal: 2),
            "claude-opus-5",
            Settings(minB2bHourly: 45m, target: 120_000m),
            ScoredAt);

        Assert.Equal(JobClass.C, classification.Class);
        Assert.Equal(JobClass.C, job.Class);
    }

    [Fact]
    public async Task ApplyAsync_ForAJobWithoutAnyCompFigure_LeavesTheEuroBoundsUnknown()
    {
        Job job = NewJob();

        await applier.ApplyAsync(job, Payload(), "claude-opus-5", Settings(minB2bHourly: 45m, target: 120_000m), ScoredAt);

        Assert.Null(job.CompMinPerYear);
        Assert.Null(job.CompMaxPerYear);
        Assert.NotNull(job.Score);
        Assert.Equal(1, job.Score.CompSignal);
    }

    [Fact]
    public async Task ApplyAsync_ForAJobRequiringUnitedStatesWorkAuthorization_RaisesTheFlagAndHoldsTheClassAtC()
    {
        Job job = NewJob();

        Classification classification = await applier.ApplyAsync(
            job,
            Payload(blockingUnknowns: [], requiresUsAuthorization: true),
            "claude-opus-5",
            Settings(),
            ScoredAt);

        Assert.Equal(JobClass.C, classification.Class);
        Assert.Equal(JobClass.C, job.Class);
        Assert.Equal<JobFlag>([JobFlag.H1, JobFlag.WA], job.Flags);
    }

    [Fact]
    public async Task ApplyAsync_WhenAReScoreSettlesWorkAuthorizationTheOtherWay_ClearsTheFlag()
    {
        Job job = NewJob();
        await applier.ApplyAsync(job, Payload(blockingUnknowns: [], requiresUsAuthorization: true), "claude-opus-5", Settings(), ScoredAt);

        Classification classification = await applier.ApplyAsync(job, Payload(blockingUnknowns: [], requiresUsAuthorization: false), "claude-opus-5", Settings(), ScoredAt);

        Assert.Equal(JobClass.A, classification.Class);
        Assert.Equal<JobFlag>([JobFlag.H1], job.Flags);
    }

    private static Job NewJob()
    {
        Job job = Job.Create("fingerprint", "https://jobs.example.com/a", "https://jobs.example.com/a", "Acme", "Staff Platform Engineer", "We run .NET on Kubernetes.", "hash-1", ScoredAt.AddDays(-1), isManual: false);
        job.RecordSource(JobSourceKind.Dataset, "dataset-1", ScoredAt.AddDays(-1));
        job.ApplyPrefilterVerdict(PrefilterState.Passed, null, [JobFlag.H1]);

        return job;
    }

    private static JobHunter.Domain.Settings Settings(decimal? minB2bHourly = null, decimal? minEmploymentAnnual = null, decimal? target = null)
    {
        JobHunter.Domain.Settings settings = JobHunter.Domain.Settings.CreateDefault();
        settings.ConfigureCompensation(minB2bHourly, minEmploymentAnnual, target);

        return settings;
    }

    private static ScorePayload Payload(
        int niche = 2,
        int level = 2,
        int stack = 2,
        int remoteTimezone = 1,
        int contractForm = 1,
        int compSignal = 1,
        int companySignal = 1,
        decimal? compMin = null,
        decimal? compMax = null,
        string? currency = null,
        string? period = null,
        string[]? blockingUnknowns = null,
        bool? requiresUsAuthorization = false)
    {
        return new ScorePayload(
            Guid.CreateVersion7().ToString(),
            new ScoreDimensionsPayload(niche, level, stack, remoteTimezone, contractForm, compSignal, companySignal),
            new ScoreFactsPayload("staff", "remote", "b2b", new ScoreCompPayload(compMin, compMax, currency, period), "Overlaps European hours.", requiresUsAuthorization, true, "Agent tooling."),
            [.. blockingUnknowns ?? ["timezone"]],
            "Platform work in the niche.");
    }
}
