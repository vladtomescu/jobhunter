using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Llm.Contracts;
using JobHunter.Pipeline;
using JobHunter.Sources;
using JobHunter.Tests.Jobs;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Tests.Pipeline;

/// <summary>Proves the one place a score becomes a stored job: skills recorded in the catalog, compensation normalized, signal recomputed, class decided, score card written.</summary>
public sealed class ScoreApplierTests : IAsyncLifetime
{
    private static readonly DateTimeOffset ScoredAt = new(2026, 9, 14, 18, 0, 0, TimeSpan.Zero);

    private readonly JobsTestHarness harness = new();
    private readonly ScoreApplier applier;

    public ScoreApplierTests()
    {
        applier = new ScoreApplier(new CompNormalizer(new FakeFxRateProvider()), harness.ContextFactory, harness.SkillGate);
    }

    public Task InitializeAsync()
    {
        return harness.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        return harness.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task ApplyAsync_ForASkillNamedByASeededAlias_FillsThatSkillsMarkWithoutAddingASkill()
    {
        int seeded = await CountSkillsAsync();

        await applier.ApplyAsync(NewJob(), Payload(skills: [new ScoreSkillPayload(" Golang ", "primary", "language", "close")]), "claude-opus-5", Settings(), ScoredAt);

        Skill go = await GetSkillAsync("Go");
        Assert.Equal(seeded, await CountSkillsAsync());
        Assert.Equal(CvMark.Close, go.CvMark);
        Assert.Equal(CvMarkSetBy.Model, go.CvMarkSetBy);
    }

    [Fact]
    public async Task ApplyAsync_ForASkillNoNameResolvesTo_CreatesItWithTheModelsCategoryAndMark()
    {
        await applier.ApplyAsync(NewJob(), Payload(skills: [new ScoreSkillPayload("Terraform", "nice_to_have", "tool", "no")]), "claude-opus-5", Settings(), ScoredAt);

        Skill terraform = await GetSkillAsync("Terraform");
        Assert.Equal<string>(["terraform"], terraform.Aliases);
        Assert.Equal(SkillCategory.Tool, terraform.Category);
        Assert.Equal(CvMark.No, terraform.CvMark);
        Assert.Equal(CvMarkSetBy.Model, terraform.CvMarkSetBy);
    }

    [Fact]
    public async Task ApplyAsync_ForANewSkillNamedTwiceInOnePayload_CreatesItOnce()
    {
        int seeded = await CountSkillsAsync();

        await applier.ApplyAsync(
            NewJob(),
            Payload(skills: [new ScoreSkillPayload("Terraform", "required", "tool", "no"), new ScoreSkillPayload("terraform", "nice_to_have", "tool", "yes")]),
            "claude-opus-5",
            Settings(),
            ScoredAt);

        Assert.Equal(seeded + 1, await CountSkillsAsync());
        Assert.Equal(CvMark.No, (await GetSkillAsync("Terraform")).CvMark);
    }

    [Fact]
    public async Task ApplyAsync_WhenTheSkillAlreadyCarriesAMark_KeepsTheMarkTheModelWouldOverwrite()
    {
        await applier.ApplyAsync(NewJob(), Payload(skills: [new ScoreSkillPayload("Kubernetes", "required", "tool", "no")]), "claude-opus-5", Settings(), ScoredAt);

        await applier.ApplyAsync(NewJob(), Payload(skills: [new ScoreSkillPayload("k8s", "required", "tool", "yes")]), "claude-opus-5", Settings(), ScoredAt);

        Skill kubernetes = await GetSkillAsync("Kubernetes");
        Assert.Equal(CvMark.No, kubernetes.CvMark);
        Assert.Equal(CvMarkSetBy.Model, kubernetes.CvMarkSetBy);
    }

    [Fact]
    public async Task ApplyAsync_WhileAnotherCatalogWriteHoldsTheGate_WaitsForItBeforeRecordingTheSkills()
    {
        TaskCompletionSource release = new();
        TaskCompletionSource entered = new();
        Task<int> holder = harness.SkillGate.RunAsync(
            async () =>
            {
                entered.SetResult();
                await release.Task;

                return 0;
            },
            CancellationToken.None);
        await entered.Task;

        Task<Classification> applying = applier.ApplyAsync(NewJob(), Payload(skills: [new ScoreSkillPayload("k8s", "required", "tool", "yes")]), "claude-opus-5", Settings(), ScoredAt);
        await Task.Delay(100);

        Assert.False(applying.IsCompleted);
        Assert.Null((await GetSkillAsync("Kubernetes")).CvMark);

        release.SetResult();
        await holder;
        await applying;

        Assert.Equal(CvMark.Yes, (await GetSkillAsync("Kubernetes")).CvMark);
    }

    [Fact]
    public async Task ApplyAsync_ForAScoredJob_StoresTheSkillsOnTheCardAsTheModelWroteThem()
    {
        Job job = NewJob();

        await applier.ApplyAsync(job, Payload(skills: [new ScoreSkillPayload("golang", "primary", "language", "close")]), "claude-opus-5", Settings(), ScoredAt);

        Assert.NotNull(job.Score);
        Assert.Equal<ScoreSkill>([new ScoreSkill("golang", "primary", "language", "close")], job.Score.Skills);
    }

    [Fact]
    public async Task ApplyAsync_ForAStrongFitWhoseCvLacksThePrimarySkill_StoresTheRecomputedStackAndHoldsTheClassAtC()
    {
        Job job = NewJob();

        Classification classification = await applier.ApplyAsync(
            job,
            Payload(stack: 2, blockingUnknowns: [], skills: [new ScoreSkillPayload("Elixir", "primary", "language", "no")]),
            "claude-opus-5",
            Settings(),
            ScoredAt);

        Assert.Equal(0, classification.Stack);
        Assert.NotNull(job.Score);
        Assert.Equal(0, job.Score.Stack);
        Assert.Equal(8, job.Score.Total);
        Assert.Equal(JobClass.C, job.Class);
    }

    [Fact]
    public async Task ApplyAsync_WhenTheCatalogAlreadyMarksAPrimarySkill_ClassifiesByThatMarkOverTheModelsJudgement()
    {
        await applier.ApplyAsync(NewJob(), Payload(skills: [new ScoreSkillPayload("Elixir", "required", "language", "yes")]), "claude-opus-5", Settings(), ScoredAt);
        Job job = NewJob();

        Classification classification = await applier.ApplyAsync(
            job,
            Payload(stack: 0, blockingUnknowns: [], skills: [new ScoreSkillPayload("elixir", "primary", "language", "no")]),
            "claude-opus-5",
            Settings(),
            ScoredAt);

        Assert.Equal(2, classification.Stack);
        Assert.Equal(JobClass.A, job.Class);
    }

    [Fact]
    public async Task ApplyAsync_WhenAJobScoredBeforeSkillsIsReScoredWithNoSkillNamed_ClearsTheScoredBeforeSkillsMark()
    {
        Job job = NewJob();
        await applier.ApplyAsync(job, Payload(), "claude-opus-5", Settings(), ScoredAt);
        job.RecordScore(job.Score! with { ScoredBeforeSkills = true }, JobClass.A);

        await applier.ApplyAsync(job, Payload(skills: []), "claude-opus-5", Settings(), ScoredAt);

        Assert.NotNull(job.Score);
        Assert.Empty(job.Score.Skills);
        Assert.False(job.Score.ScoredBeforeSkills);
    }

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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public async Task ApplyAsync_ForABandJobWithoutCompanySignal_KeepsClassBWhateverTheEndClientFact(bool? endClientNamed)
    {
        Job job = NewJob();

        Classification classification = await applier.ApplyAsync(job, Payload(companySignal: 0, blockingUnknowns: [], endClientNamed: endClientNamed), "claude-opus-5", Settings(), ScoredAt);

        Assert.Equal(JobClass.B, classification.Class);
        Assert.Equal(JobClass.B, job.Class);
    }

    private static Job NewJob()
    {
        Job job = Job.Create("fingerprint", "https://jobs.example.com/a", "https://jobs.example.com/a", "Acme", "Staff Platform Engineer", "We run .NET on Kubernetes.", "hash-1", ScoredAt.AddDays(-1), isManual: false);
        job.RecordSource(JobSourceKind.Dataset, "dataset-1", ScoredAt.AddDays(-1));
        job.ApplyPrefilterVerdict(PrefilterState.Passed, null, [JobFlag.H1]);

        return job;
    }

    private static JobHunter.Domain.Settings Settings(decimal? minB2bHourly = null, decimal? minEmploymentAnnual = null, decimal? target = null, decimal? highPayThresholdPerYear = 110_000m)
    {
        JobHunter.Domain.Settings settings = JobHunter.Domain.Settings.CreateDefault();
        settings.ConfigureCompensation(minB2bHourly, minEmploymentAnnual, target);
        settings.ConfigureCandidate(null, string.Empty, true, true, "en", settings.BaseCurrency, string.Empty, ContractPreference.Either, false, highPayThresholdPerYear, settings.TitleIncludeTerms, settings.TitleExcludeTerms);

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
        bool? requiresUsAuthorization = false,
        bool? endClientNamed = true,
        ScoreSkillPayload[]? skills = null)
    {
        return new ScorePayload(
            Guid.CreateVersion7().ToString(),
            new ScoreDimensionsPayload(niche, level, stack, remoteTimezone, contractForm, compSignal, companySignal),
            new ScoreFactsPayload("staff", "remote", "b2b", new ScoreCompPayload(compMin, compMax, currency, period), "Overlaps European hours.", requiresUsAuthorization, endClientNamed, "Agent tooling."),
            [.. skills ?? []],
            [.. blockingUnknowns ?? ["timezone"]],
            "Platform work in the niche.");
    }

    private async Task<Skill> GetSkillAsync(string name)
    {
        await using JobHunterDbContext context = await harness.ContextFactory.CreateDbContextAsync();

        return await context.Skills.AsNoTracking().SingleAsync(skill => skill.Name == name);
    }

    private async Task<int> CountSkillsAsync()
    {
        await using JobHunterDbContext context = await harness.ContextFactory.CreateDbContextAsync();

        return await context.Skills.CountAsync();
    }
}
