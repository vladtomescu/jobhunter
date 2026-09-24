using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Jobs;
using JobHunter.Llm;
using JobHunter.Pipeline;
using JobHunter.Settings;
using JobHunter.Tests.Pipeline;
using JobHunter.Tests.Refresh;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Tests.Jobs;

/// <summary>Proves that a change of base currency followed by a recompute converts every job's pay from its raw amounts, converts the bounds, reclassifies from the stored score cards and never calls the scorer.</summary>
/// <remarks>The fake rates are 1 EUR = 1.25 USD = 0.80 GBP, so a dollar base reads euro × 1.25 and pound ÷ 0.64.</remarks>
public sealed class CompRecomputeTests : IAsyncLifetime
{
    private static readonly DateTimeOffset SeenAt = new(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);

    private readonly string dataFolder = Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeJobScorer scorer = new();
    private readonly ServiceProvider provider;

    public CompRecomputeTests()
    {
        ServiceCollection services = new();
        services.AddData();
        services.AddSettings();
        services.AddPipeline();
        services.AddJobs();
        services.AddSingleton(new DataPaths(dataFolder));
        services.AddSingleton<IFxRateProvider>(new FakeFxRateProvider());
        services.AddSingleton<IJobScorer>(scorer);

        provider = services.BuildServiceProvider();
    }

    private CompRecomputeService Recompute => provider.GetRequiredService<CompRecomputeService>();

    private SettingsService SettingsService => provider.GetRequiredService<SettingsService>();

    public async Task InitializeAsync()
    {
        await provider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        await SettingsService.ApplyAsync(settings =>
        {
            settings.ConfigureCompensation(45m, 65_000m, 120_000m);
            ConfigureBaseCurrency(settings, "EUR");
        });
    }

    [Fact]
    public async Task RecomputeAsync_AfterASwitchFromEuroToDollar_ConvertsPayBoundsAndClassesWithoutCallingTheScorer()
    {
        Job dollarJob = ScoredJob("Acme", 120_000m, "USD", CompPeriod.Year, 96_000m, "b2b", Scores(2, 2, 2, 1, 1, 1, 1), JobClass.A);
        Job poundJob = ScoredJob("Globex", 80_000m, "GBP", CompPeriod.Year, 100_000m, "employment", Scores(1, 1, 1, 1, 1, 1, 2), JobClass.B);
        Job staleEuroJob = ScoredJob("Initech", 55m, "EUR", CompPeriod.Hour, 96_800m, "b2b", Scores(1, 1, 1, 1, 1, 0, 1), JobClass.C);
        Job unscoredJob = ListedJobs.NewJob("Umbrella", "Backend Engineer", SeenAt);
        unscoredJob.ApplyPrefilterVerdict(PrefilterState.Passed, null, []);
        unscoredJob.RecordCompensation(1_000m, null, "EUR", CompPeriod.Month, 12_000m, null);
        await SaveAsync(dollarJob, poundJob, staleEuroJob, unscoredJob);
        await SwitchBaseCurrencyAsync("USD");

        CompRecomputeResult result = await Recompute.RecomputeAsync();

        Assert.Null(result.Refusal);
        Assert.Equal(4, result.JobsWithPay);
        Assert.Equal(1, result.ClassesChanged);

        Job dollar = await GetJobAsync(dollarJob.Id);
        Assert.Equal(120_000m, dollar.CompMinPerYear);
        Assert.Equal(1, dollar.Score!.CompSignal);
        Assert.Equal(JobClass.A, dollar.Class);

        Job pound = await GetJobAsync(poundJob.Id);
        Assert.Equal(125_000m, pound.CompMinPerYear);
        Assert.Contains(JobFlag.HighPay, pound.Flags);
        Assert.Equal(JobClass.B, pound.Class);

        Job euro = await GetJobAsync(staleEuroJob.Id);
        Assert.Equal(121_000m, euro.CompMinPerYear);
        Assert.Equal(1, euro.Score!.CompSignal);
        Assert.Equal(7, euro.Score.Total);
        Assert.Equal(JobClass.B, euro.Class);
        Assert.Equal(SeenAt, euro.Score.ScoredAt);

        Job unscored = await GetJobAsync(unscoredJob.Id);
        Assert.Equal(15_000m, unscored.CompMinPerYear);
        Assert.Null(unscored.Class);
        Assert.Equal(ScoringState.Unscored, unscored.Scoring);

        JobHunter.Domain.Settings settings = await SettingsService.GetAsync();
        Assert.Equal(56.25m, settings.MinContractorHourly);
        Assert.Equal(81_250m, settings.MinEmploymentAnnual);
        Assert.Equal(150_000m, settings.TargetAnnual);
        Assert.Equal(112_500m, settings.HighPayThresholdPerYear);
        Assert.Equal("USD", settings.CompComputedInCurrency);

        Assert.Empty(scorer.Requests);
    }

    [Fact]
    public async Task RecomputeAsync_AfterASwitchToDollarAndBack_ReturnsEveryFigureAndClass()
    {
        Job poundJob = ScoredJob("Globex", 80_000m, "GBP", CompPeriod.Year, 100_000m, "employment", Scores(1, 1, 1, 1, 1, 1, 2), JobClass.B);
        await SaveAsync(poundJob);
        await SwitchBaseCurrencyAsync("USD");
        await Recompute.RecomputeAsync();
        await SwitchBaseCurrencyAsync("EUR");

        CompRecomputeResult result = await Recompute.RecomputeAsync();

        Assert.Equal(0, result.ClassesChanged);
        Job pound = await GetJobAsync(poundJob.Id);
        Assert.Equal(100_000m, pound.CompMinPerYear);
        Assert.Equal(JobClass.B, pound.Class);

        JobHunter.Domain.Settings settings = await SettingsService.GetAsync();
        Assert.Equal(45m, settings.MinContractorHourly);
        Assert.Equal(65_000m, settings.MinEmploymentAnnual);
        Assert.Equal(120_000m, settings.TargetAnnual);
        Assert.Equal(90_000m, settings.HighPayThresholdPerYear);
        Assert.Equal("EUR", settings.CompComputedInCurrency);
    }

    [Fact]
    public async Task RecomputeAsync_ForABaseCurrencyWithoutARate_RefusesAndChangesNothing()
    {
        Job dollarJob = ScoredJob("Acme", 120_000m, "USD", CompPeriod.Year, 96_000m, "b2b", Scores(2, 2, 2, 1, 1, 1, 1), JobClass.A);
        await SaveAsync(dollarJob);
        await SwitchBaseCurrencyAsync("XYZ");

        CompRecomputeResult result = await Recompute.RecomputeAsync();

        Assert.NotNull(result.Refusal);
        Assert.Equal(96_000m, (await GetJobAsync(dollarJob.Id)).CompMinPerYear);
        JobHunter.Domain.Settings settings = await SettingsService.GetAsync();
        Assert.Equal(45m, settings.MinContractorHourly);
        Assert.Equal("EUR", settings.CompComputedInCurrency);
    }

    public async Task DisposeAsync()
    {
        await provider.DisposeAsync();
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(dataFolder))
        {
            Directory.Delete(dataFolder, recursive: true);
        }
    }

    private static int[] Scores(int niche, int level, int stack, int remoteTimezone, int contractForm, int compSignal, int companySignal)
    {
        return [niche, level, stack, remoteTimezone, contractForm, compSignal, companySignal];
    }

    private static Job ScoredJob(string company, decimal amount, string currency, CompPeriod period, decimal eurPerYear, string employmentType, int[] scores, JobClass jobClass)
    {
        Job job = ListedJobs.NewJob(company, "Platform Engineer", SeenAt);
        job.ApplyPrefilterVerdict(PrefilterState.Passed, null, []);
        job.RecordCompensation(amount, null, currency, period, eurPerYear, null);

        ScoreCard card = new(
            scores[0], scores[1], scores[2], scores[3], scores[4], scores[5], scores[6], scores.Sum(),
            "Fit.", "senior", "remote", employmentType, amount, null, currency, period,
            string.Empty, false, true, string.Empty, [], "claude-opus-5", SeenAt, job.DescriptionHash);
        job.RecordScore(card, jobClass);

        return job;
    }

    private static void ConfigureBaseCurrency(JobHunter.Domain.Settings settings, string baseCurrency)
    {
        settings.ConfigureCandidate(null, true, true, "en", baseCurrency, string.Empty, ContractPreference.Contractor, false, 90_000m, settings.TitleIncludeTerms, settings.TitleExcludeTerms);
    }

    private async Task SwitchBaseCurrencyAsync(string baseCurrency)
    {
        await SettingsService.ApplyAsync(settings => settings.ConfigureCandidate(null, true, true, "en", baseCurrency, string.Empty, ContractPreference.Contractor, false, settings.HighPayThresholdPerYear, settings.TitleIncludeTerms, settings.TitleExcludeTerms));
    }

    private async Task SaveAsync(params Job[] jobs)
    {
        await using JobHunterDbContext context = await provider.GetRequiredService<IDbContextFactory<JobHunterDbContext>>().CreateDbContextAsync();
        context.Jobs.AddRange(jobs);
        await context.SaveChangesAsync();
    }

    private async Task<Job> GetJobAsync(Guid jobId)
    {
        await using JobHunterDbContext context = await provider.GetRequiredService<IDbContextFactory<JobHunterDbContext>>().CreateDbContextAsync();

        return await context.Jobs.AsNoTracking().SingleAsync(job => job.Id == jobId);
    }
}
