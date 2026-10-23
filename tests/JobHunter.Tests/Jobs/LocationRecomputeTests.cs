using JobHunter.Domain;
using JobHunter.Jobs;
using JobHunter.Pipeline;
using JobHunter.Sources;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobHunter.Tests.Jobs;

/// <summary>Proves that the location recompute drops a passed job the candidate's places no longer cover, passes again a job a geography reason dropped once the places cover it, judging its age as of its first sighting, leaves pursued, manual and otherwise dropped jobs alone, and counts what it changed.</summary>
public sealed class LocationRecomputeTests
{
    private const string Description = "We build payment services in Kotlin on Kubernetes for merchants across Europe.";

    private static readonly DateTimeOffset SeenAt = new(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_ForAPassedJobOutsideThePlaces_DropsItWithTheGeographyReason()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await ConfigurePlacesAsync(harness, "Germany");
        Job job = PlacedJob("Austin, TX, United States", "US", isRemote: false);
        job.ApplyPrefilterVerdict(PrefilterState.Passed, null, [JobFlag.H4]);
        await harness.SaveAsync(job);

        int changed = await NewRecompute(harness).RunAsync();

        Job stored = await harness.GetJobAsync(job.Id);
        Assert.Equal(1, changed);
        Assert.Equal(PrefilterState.Dropped, stored.Prefilter);
        Assert.Equal(GeographyRules.OutsidePlacesReason, stored.DropReason);
        Assert.Equal(JobClass.D, stored.Class);
    }

    [Theory]
    [InlineData("Berlin, Germany", "DE", false, GeographyRules.OutsidePlacesReason)]
    [InlineData("Berlin, Germany", "DE", false, "onsite outside your region, no comp, no relocation")]
    [InlineData("Remote (US only)", null, true, "remote restricted to the United States")]
    [InlineData("Remote, Europe", null, true, "remote restricted to Europe")]
    [InlineData("Remote - India, CET overlap", null, true, "remote restricted to a region that excludes Europe and the United States")]
    public async Task RunAsync_ForAnUnscoredJobDroppedForAGeographyReasonThePlacesNowCover_PassesItAgainToWaitForScoring(string location, string? countryIso, bool isRemote, string storedReason)
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await ConfigurePlacesAsync(harness, "Germany");
        Job job = PlacedJob(location, countryIso, isRemote);
        job.ApplyPrefilterVerdict(PrefilterState.Dropped, storedReason, [JobFlag.H4]);
        await harness.SaveAsync(job);

        int changed = await NewRecompute(harness).RunAsync();

        Job stored = await harness.GetJobAsync(job.Id);
        Assert.Equal(1, changed);
        Assert.Equal(PrefilterState.Passed, stored.Prefilter);
        Assert.Null(stored.DropReason);
        Assert.Null(stored.Class);
        Assert.Equal(ScoringState.Unscored, stored.Scoring);
        Assert.Equal<JobFlag>([JobFlag.H4], stored.Flags);
    }

    [Fact]
    public async Task RunAsync_ForAScoredJobDroppedForAGeographyReasonThePlacesNowCover_PassesItAgainAndReclassifiesItFromItsCard()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await ConfigurePlacesAsync(harness, "Germany");
        Job job = PlacedJob("Berlin, Germany", "DE", isRemote: false);
        job.ApplyPrefilterVerdict(PrefilterState.Passed, null, [JobFlag.H4]);
        job.RecordScore(ListedJobs.NewScoreCard(SeenAt), JobClass.A);
        job.Drop(GeographyRules.OutsidePlacesReason);
        await harness.SaveAsync(job);

        int changed = await NewRecompute(harness).RunAsync();

        Job stored = await harness.GetJobAsync(job.Id);
        Assert.Equal(1, changed);
        Assert.Equal(PrefilterState.Passed, stored.Prefilter);
        Assert.Null(stored.DropReason);
        Assert.Equal(ScoringState.Scored, stored.Scoring);
        Assert.Equal(JobClass.A, stored.Class);
    }

    [Fact]
    public async Task RunAsync_ForAPursuedJobOutsideThePlaces_LeavesItsStateAndClass()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await ConfigurePlacesAsync(harness, "Germany");
        Job job = PlacedJob("Austin, TX, United States", "US", isRemote: false);
        job.ApplyPrefilterVerdict(PrefilterState.Passed, null, [JobFlag.H4]);
        job.RecordScore(ListedJobs.NewScoreCard(SeenAt), JobClass.A);
        job.Pursue(SeenAt.AddDays(1));
        await harness.SaveAsync(job);

        int changed = await NewRecompute(harness).RunAsync();

        Job stored = await harness.GetJobAsync(job.Id);
        Assert.Equal(0, changed);
        Assert.Equal(PrefilterState.Passed, stored.Prefilter);
        Assert.Equal(JobClass.A, stored.Class);
    }

    [Fact]
    public async Task RunAsync_ForAManualJobOutsideThePlaces_LeavesItPassed()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await ConfigurePlacesAsync(harness, "Germany");
        Job job = PlacedJob("Austin, TX, United States", "US", isRemote: false, isManual: true);
        job.ApplyPrefilterVerdict(PrefilterState.Passed, null, [JobFlag.H4]);
        await harness.SaveAsync(job);

        int changed = await NewRecompute(harness).RunAsync();

        Job stored = await harness.GetJobAsync(job.Id);
        Assert.Equal(0, changed);
        Assert.Equal(PrefilterState.Passed, stored.Prefilter);
        Assert.Null(stored.DropReason);
    }

    [Theory]
    [InlineData(Prefilter.AgencyReason)]
    [InlineData("title excluded: frontend")]
    public async Task RunAsync_ForAJobDroppedForAnotherReasonInsideThePlaces_LeavesItDropped(string storedReason)
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await ConfigurePlacesAsync(harness, "Germany");
        Job job = PlacedJob("Berlin, Germany", "DE", isRemote: false);
        job.ApplyPrefilterVerdict(PrefilterState.Dropped, storedReason, [JobFlag.H4]);
        await harness.SaveAsync(job);

        int changed = await NewRecompute(harness).RunAsync();

        Job stored = await harness.GetJobAsync(job.Id);
        Assert.Equal(0, changed);
        Assert.Equal(PrefilterState.Dropped, stored.Prefilter);
        Assert.Equal(storedReason, stored.DropReason);
    }

    [Fact]
    public async Task RunAsync_ForAJobFirstSeenWithinTheAgeLimitOfItsPostingLongAgo_JudgesItsAgeAsOfItsFirstSighting()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await ConfigurePlacesAsync(harness, "Germany");
        DateTimeOffset firstSeenAt = new(2025, 3, 1, 8, 0, 0, TimeSpan.Zero);
        Job job = PlacedJob("Berlin, Germany", "DE", isRemote: false, firstSeenAt: firstSeenAt);
        job.RecordPostingFacts(null, null, null, [], null, firstSeenAt.AddDays(-10));
        job.ApplyPrefilterVerdict(PrefilterState.Dropped, GeographyRules.OutsidePlacesReason, [JobFlag.H4]);
        await harness.SaveAsync(job);

        int changed = await NewRecompute(harness).RunAsync();

        Job stored = await harness.GetJobAsync(job.Id);
        Assert.Equal(1, changed);
        Assert.Equal(PrefilterState.Passed, stored.Prefilter);
        Assert.Null(stored.DropReason);
    }

    [Fact]
    public async Task RunAsync_ForJobsDroppedAndPassedAgain_CountsBothAndNothingOnASecondRun()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await ConfigurePlacesAsync(harness, "Germany");
        Job outside = PlacedJob("Austin, TX, United States", "US", isRemote: false);
        outside.ApplyPrefilterVerdict(PrefilterState.Passed, null, [JobFlag.H4]);
        Job covered = PlacedJob("Munich, Germany", "DE", isRemote: false);
        covered.ApplyPrefilterVerdict(PrefilterState.Dropped, GeographyRules.OutsidePlacesReason, [JobFlag.H4]);
        Job unchanged = PlacedJob("Hamburg, Germany", "DE", isRemote: false);
        unchanged.ApplyPrefilterVerdict(PrefilterState.Passed, null, [JobFlag.H4]);
        await harness.SaveAsync(outside, covered, unchanged);
        LocationRecompute recompute = NewRecompute(harness);

        int changed = await recompute.RunAsync();
        int changedAgain = await recompute.RunAsync();

        Assert.Equal(2, changed);
        Assert.Equal(0, changedAgain);
        Assert.Equal(PrefilterState.Dropped, (await harness.GetJobAsync(outside.Id)).Prefilter);
        Assert.Equal(PrefilterState.Passed, (await harness.GetJobAsync(covered.Id)).Prefilter);
        Assert.Equal(PrefilterState.Passed, (await harness.GetJobAsync(unchanged.Id)).Prefilter);
    }

    private static LocationRecompute NewRecompute(JobsTestHarness harness)
    {
        return new LocationRecompute(harness.ContextFactory, new Prefilter(), NullLogger<LocationRecompute>.Instance);
    }

    /// <summary>A job from the dataset source, which leaves the remote policy to the place, at the given place, with a description long enough to count as a posting.</summary>
    private static Job PlacedJob(string location, string? countryIso, bool isRemote, bool isManual = false, DateTimeOffset? firstSeenAt = null)
    {
        Job job = ListedJobs.NewJob("Acme", "Senior Backend Engineer", firstSeenAt ?? SeenAt, JobSourceKind.Dataset, isManual, Description);
        job.RecordPlace(location, countryIso, null, isRemote, "en");

        return job;
    }

    private static async Task ConfigurePlacesAsync(JobsTestHarness harness, string relocationPlaces)
    {
        await harness.SettingsService.ApplyAsync(settings =>
        {
            settings.ConfigureCandidate(
                "NL",
                "Utrecht",
                "en",
                "EUR",
                string.Empty,
                ContractPreference.Either,
                hasUnitedStatesWorkAuthorization: false,
                highPayThresholdPerYear: null,
                JobHunter.Domain.Settings.DefaultTitleIncludeTerms,
                JobHunter.Domain.Settings.DefaultTitleExcludeTerms);
            settings.ConfigureRelocationPlaces(relocationPlaces);
        });
    }
}
