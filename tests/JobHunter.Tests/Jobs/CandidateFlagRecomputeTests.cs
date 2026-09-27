using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobHunter.Tests.Jobs;

/// <summary>Proves that the candidate flag recompute gives stored jobs the stack-match, home-city and high-pay flags their current settings imply, clears stale ones, follows a settings change without a restart, and changes nothing on a second run.</summary>
public sealed class CandidateFlagRecomputeTests
{
    private static readonly DateTimeOffset SeenAt = new(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_ForJobsStoredBeforeTheCandidateFlagsExisted_RaisesThemAndKeepsTheOtherFlags()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await ConfigureCandidateAsync(harness, homeCountryIso: "NL", homeCity: "Utrecht", stackKeywords: "Java, Kotlin", highPayThresholdPerYear: 110_000m);
        Job stackJob = ListedJobs.NewJob("Acme", "Senior Kotlin Engineer", SeenAt);
        Job wellPaidJob = ListedJobs.NewJob("Globex", "Staff Platform Engineer", SeenAt);
        wellPaidJob.RecordCompensation(null, 120_000m, "EUR", CompPeriod.Year, null, 120_000m);
        Job homeCityJob = ListedJobs.NewJob("Initech", "Platform Engineer", SeenAt);
        homeCityJob.RecordPlace("Utrecht, Netherlands", "NL", null, false, "en");
        await harness.SaveAsync(stackJob, wellPaidJob, homeCityJob);
        await OverwriteFlagsAsync(harness, stackJob.Id, """["H1","CU"]""");
        await OverwriteFlagsAsync(harness, wellPaidJob.Id, """["H1","WA"]""");
        await OverwriteFlagsAsync(harness, homeCityJob.Id, """["H4"]""");

        int changed = await NewRecompute(harness).RunAsync();

        Assert.Equal(3, changed);
        Assert.Equal<JobFlag>([JobFlag.H1, JobFlag.CU, JobFlag.StackMatch], (await harness.GetJobAsync(stackJob.Id)).Flags);
        Assert.Equal<JobFlag>([JobFlag.H1, JobFlag.WA, JobFlag.HighPay], (await harness.GetJobAsync(wellPaidJob.Id)).Flags);
        Assert.Equal<JobFlag>([JobFlag.H4, JobFlag.HomeCity], (await harness.GetJobAsync(homeCityJob.Id)).Flags);
    }

    [Fact]
    public async Task RunAsync_ForAJobWhoseStoredFlagsNoLongerHold_ClearsThem()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await ConfigureCandidateAsync(harness, homeCountryIso: "NL", homeCity: "Utrecht", stackKeywords: "Java, Kotlin", highPayThresholdPerYear: 110_000m);
        Job job = ListedJobs.NewJob("Initech", "Backend Engineer", SeenAt);
        await harness.SaveAsync(job);
        await OverwriteFlagsAsync(harness, job.Id, """["H3","StackMatch","HomeCity","HighPay"]""");

        int changed = await NewRecompute(harness).RunAsync();

        Assert.Equal(1, changed);
        Assert.Equal<JobFlag>([JobFlag.H3], (await harness.GetJobAsync(job.Id)).Flags);
    }

    [Fact]
    public async Task RunAsync_ASecondTime_ChangesNothing()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await ConfigureCandidateAsync(harness, homeCountryIso: "NL", homeCity: "Utrecht", stackKeywords: "Java, Kotlin", highPayThresholdPerYear: 110_000m);
        Job job = ListedJobs.NewJob("Acme", "Senior Java Engineer", SeenAt);
        await harness.SaveAsync(job);
        await OverwriteFlagsAsync(harness, job.Id, """["H1"]""");
        CandidateFlagRecompute recompute = NewRecompute(harness);
        await recompute.RunAsync();

        int changed = await recompute.RunAsync();

        Assert.Equal(0, changed);
    }

    [Fact]
    public async Task RunAsync_AfterTheStackKeywordsSettingChanges_UpdatesTheFlagsWithoutARestart()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await ConfigureCandidateAsync(harness, homeCountryIso: null, homeCity: string.Empty, stackKeywords: "Go", highPayThresholdPerYear: null);
        Job job = ListedJobs.NewJob("Acme", "Senior Kotlin Engineer", SeenAt);
        await harness.SaveAsync(job);
        CandidateFlagRecompute recompute = NewRecompute(harness);
        await recompute.RunAsync();
        Assert.DoesNotContain(JobFlag.StackMatch, (await harness.GetJobAsync(job.Id)).Flags);

        await ConfigureCandidateAsync(harness, homeCountryIso: null, homeCity: string.Empty, stackKeywords: "Java, Kotlin", highPayThresholdPerYear: null);
        int changed = await recompute.RunAsync();

        Assert.Equal(1, changed);
        Assert.Contains(JobFlag.StackMatch, (await harness.GetJobAsync(job.Id)).Flags);
    }

    [Fact]
    public async Task RunAsync_ForAnOnsiteJobInAnotherCityOfTheHomeCountry_ClearsTheHomeCityFlag()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await ConfigureCandidateAsync(harness, homeCountryIso: "NL", homeCity: "Utrecht", stackKeywords: "Java, Kotlin", highPayThresholdPerYear: null);
        Job job = ListedJobs.NewJob("Initech", "Platform Engineer", SeenAt);
        job.RecordPlace("Rotterdam, Netherlands", "NL", null, false, "en");
        await harness.SaveAsync(job);
        await OverwriteFlagsAsync(harness, job.Id, """["H4","HomeCity"]""");

        int changed = await NewRecompute(harness).RunAsync();

        Assert.Equal(1, changed);
        Assert.Equal<JobFlag>([JobFlag.H4], (await harness.GetJobAsync(job.Id)).Flags);
    }

    [Fact]
    public async Task RunAsync_AfterTheHomeCitySettingChanges_RaisesTheFlagForAPlaceWrittenWithDiacritics()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await ConfigureCandidateAsync(harness, homeCountryIso: "PL", homeCity: string.Empty, stackKeywords: "Java", highPayThresholdPerYear: null);
        Job job = ListedJobs.NewJob("Initech", "Platform Engineer", SeenAt);
        job.RecordPlace("Poznań, Poland - Hybrid", "PL", null, false, "en");
        await harness.SaveAsync(job);
        await OverwriteFlagsAsync(harness, job.Id, """["H4"]""");
        CandidateFlagRecompute recompute = NewRecompute(harness);
        await recompute.RunAsync();
        Assert.DoesNotContain(JobFlag.HomeCity, (await harness.GetJobAsync(job.Id)).Flags);

        await ConfigureCandidateAsync(harness, homeCountryIso: "PL", homeCity: "Poznan", stackKeywords: "Java", highPayThresholdPerYear: null);
        int changed = await recompute.RunAsync();

        Assert.Equal(1, changed);
        Assert.Equal<JobFlag>([JobFlag.H4, JobFlag.HomeCity], (await harness.GetJobAsync(job.Id)).Flags);
    }

    private static CandidateFlagRecompute NewRecompute(JobsTestHarness harness)
    {
        return new CandidateFlagRecompute(harness.ContextFactory, NullLogger<CandidateFlagRecompute>.Instance);
    }

    private static async Task ConfigureCandidateAsync(JobsTestHarness harness, string? homeCountryIso, string homeCity, string stackKeywords, decimal? highPayThresholdPerYear)
    {
        await harness.SettingsService.ApplyAsync(settings => settings.ConfigureCandidate(
            homeCountryIso,
            homeCity,
            true,
            true,
            "en",
            "EUR",
            stackKeywords,
            ContractPreference.Either,
            hasUnitedStatesWorkAuthorization: false,
            highPayThresholdPerYear,
            JobHunter.Domain.Settings.DefaultTitleIncludeTerms,
            JobHunter.Domain.Settings.DefaultTitleExcludeTerms));
    }

    /// <summary>Writes the flags column directly, the way a job stored before the candidate flags existed looks.</summary>
    private static async Task OverwriteFlagsAsync(JobsTestHarness harness, Guid jobId, string flagsJson)
    {
        await using JobHunterDbContext context = await harness.ContextFactory.CreateDbContextAsync();
        await context.Database.ExecuteSqlAsync($"UPDATE Jobs SET Flags = {flagsJson} WHERE Id = {jobId}");
    }
}
