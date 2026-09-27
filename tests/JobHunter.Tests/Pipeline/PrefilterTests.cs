using JobHunter.Domain;
using JobHunter.Pipeline;
using JobHunter.Sources;

namespace JobHunter.Tests.Pipeline;

/// <summary>Proves the deterministic rules that run before any model call: what is dropped, what survives, and which flags a posting carries, for a candidate in the Netherlands, for a second candidate in Berlin and for a candidate in the United States.</summary>
public sealed class PrefilterTests
{
    private static readonly DateTimeOffset EvaluatedAt = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly Prefilter prefilter = new();

    [Fact]
    public void Evaluate_ForARemoteEuropeanRole_Passes()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(location: "Remote, Europe", isRemote: true));

        Assert.Equal(PrefilterState.Passed, verdict.State);
        Assert.Null(verdict.DropReason);
        Assert.DoesNotContain(JobFlag.H3, verdict.Flags);
        Assert.DoesNotContain(JobFlag.H4, verdict.Flags);
    }

    [Fact]
    public void Evaluate_ForARemoteRoleOpenToTheUnitedStatesOnly_PassesWithTheHoursFlag()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(location: "Remote (US only)", isRemote: true));

        Assert.Equal(PrefilterState.Passed, verdict.State);
        Assert.Contains(JobFlag.H3, verdict.Flags);
    }

    [Fact]
    public void Evaluate_ForARemoteUnitedStatesRoleWhenThoseAreSwitchedOff_Drops()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(location: "Remote (US only)", isRemote: true, candidate: CandidateProfiles.NetherlandsJavaWithoutUnitedStatesRemote));

        Assert.Equal(PrefilterState.Dropped, verdict.State);
        Assert.Equal(GeographyRules.UnitedStatesOnlyReason, verdict.DropReason);
    }

    [Fact]
    public void Evaluate_ForARemoteRoleRestrictedToLatinAmerica_Drops()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(location: "Remote (LATAM only)", isRemote: true));

        Assert.Equal(PrefilterState.Dropped, verdict.State);
        Assert.Equal(GeographyRules.RegionExcludedReason, verdict.DropReason);
    }

    [Fact]
    public void Evaluate_ForAnOnsiteRoleInBerlin_PassesWithTheRelocationFlag()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(location: "Berlin, Germany", countryIso: "DE", isRemote: false));

        Assert.Equal(PrefilterState.Passed, verdict.State);
        Assert.Contains(JobFlag.H4, verdict.Flags);
        Assert.DoesNotContain(JobFlag.HomeCity, verdict.Flags);
    }

    [Fact]
    public void Evaluate_ForAnOnsiteRoleInAustinWithStatedComp_PassesWithTheRelocationFlag()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(location: "Austin, TX, United States", countryIso: "US", isRemote: false, hasStatedComp: true));

        Assert.Equal(PrefilterState.Passed, verdict.State);
        Assert.Contains(JobFlag.H4, verdict.Flags);
        Assert.Contains(JobFlag.H3, verdict.Flags);
        Assert.DoesNotContain(JobFlag.CU, verdict.Flags);
    }

    [Fact]
    public void Evaluate_ForAnOnsiteRoleInAustinWithoutCompOrRelocation_Drops()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(location: "Austin, TX, United States", countryIso: "US", isRemote: false));

        Assert.Equal(PrefilterState.Dropped, verdict.State);
        Assert.Equal(GeographyRules.OnsiteWithoutBasisReason, verdict.DropReason);
    }

    [Fact]
    public void Evaluate_ForAnOnsiteRoleAbroadThatOffersRelocation_Passes()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(
            Input(location: "Austin, TX, United States", countryIso: "US", isRemote: false, description: "We offer relocation and visa sponsorship."));

        Assert.Equal(PrefilterState.Passed, verdict.State);
        Assert.Contains(JobFlag.H4, verdict.Flags);
    }

    [Fact]
    public void Evaluate_ForAHybridRoleInTheHomeCity_PassesWithTheRelocationAndHomeCityFlags()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(location: "Utrecht, Netherlands - Hybrid", countryIso: "NL"));

        Assert.Equal(PrefilterState.Passed, verdict.State);
        Assert.Contains(JobFlag.H4, verdict.Flags);
        Assert.Contains(JobFlag.HomeCity, verdict.Flags);
    }

    [Fact]
    public void Evaluate_ForAPostingFromARemoteOnlyBoardWhoseLocationSaysHybrid_StaysRemoteAndPasses()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(location: "Hybrid - Austin", source: JobSourceKind.RemoteOk));

        Assert.Equal(PrefilterState.Passed, verdict.State);
        Assert.Null(verdict.DropReason);
        Assert.DoesNotContain(JobFlag.H4, verdict.Flags);
        Assert.Contains(JobFlag.H3, verdict.Flags);
    }

    [Fact]
    public void Evaluate_ForAManualJobThatEveryRuleWouldDrop_Passes()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(
            title: "Engineering Manager",
            location: "Austin, TX, United States",
            countryIso: "US",
            isRemote: false,
            language: "de",
            postedAt: EvaluatedAt.AddDays(-200),
            isManual: true));

        Assert.Equal(PrefilterState.Passed, verdict.State);
        Assert.Null(verdict.DropReason);
        Assert.Contains(JobFlag.H4, verdict.Flags);
    }

    [Theory]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("es")]
    public void Evaluate_ForAPostingInAnotherLanguage_Drops(string language)
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(language: language));

        Assert.Equal(Prefilter.LanguageReason, verdict.DropReason);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("nl")]
    [InlineData("en-US")]
    [InlineData(null)]
    public void Evaluate_ForAPostingInAnAcceptedLanguage_Passes(string? language)
    {
        Assert.Equal(PrefilterState.Passed, prefilter.Evaluate(Input(language: language)).State);
    }

    [Fact]
    public void Evaluate_ForAPostingOlderThanTheAgeLimit_Drops()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(postedAt: EvaluatedAt.AddDays(-46)));

        Assert.Equal(Prefilter.AgeReason, verdict.DropReason);
    }

    [Fact]
    public void Evaluate_ForAPostingInsideTheAgeLimit_Passes()
    {
        Assert.Equal(PrefilterState.Passed, prefilter.Evaluate(Input(postedAt: EvaluatedAt.AddDays(-44))).State);
    }

    [Fact]
    public void Evaluate_ForAPostingWithoutAPostedDate_Passes()
    {
        Assert.Equal(PrefilterState.Passed, prefilter.Evaluate(Input(postedAt: null, withoutPostedDate: true)).State);
    }

    [Fact]
    public void Evaluate_ForAnExcludedTitle_DropsWithTheRuleThatCaughtIt()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(title: "Senior Frontend Engineer"));

        Assert.Equal("title excluded: frontend", verdict.DropReason);
    }

    [Fact]
    public void Evaluate_ForATitleWithoutAnEngineeringTerm_Drops()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(title: "Customer Success Specialist"));

        Assert.Equal(Prefilter.NoIncludeTermReason, verdict.DropReason);
    }

    [Theory]
    [InlineData("Our client is a leading European bank.")]
    [InlineData("You would be rotating projects every six months.")]
    [InlineData("We are hiring for a confidential client in the payments space.")]
    public void Evaluate_ForAnAgencyPosting_Drops(string description)
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(description: description, location: "Remote, Europe", isRemote: true));

        Assert.Equal(Prefilter.AgencyReason, verdict.DropReason);
    }

    [Fact]
    public void Evaluate_ForASeniorTitle_RaisesTheLevelFlag()
    {
        Assert.Contains(JobFlag.H1, prefilter.Evaluate(Input(title: "Staff Software Engineer")).Flags);
    }

    [Fact]
    public void Evaluate_ForATitleWithoutALevel_DoesNotRaiseTheLevelFlag()
    {
        Assert.DoesNotContain(JobFlag.H1, prefilter.Evaluate(Input(title: "Backend Engineer")).Flags);
    }

    [Theory]
    [InlineData("This is a permanent position, no B2B.")]
    [InlineData("We hire on an employment contract only.")]
    [InlineData("Employees only, no contractors.")]
    public void Evaluate_ForEmploymentOnlyWording_RaisesTheContractFlag(string description)
    {
        Assert.Contains(JobFlag.H2, prefilter.Evaluate(Input(description: description)).Flags);
    }

    [Fact]
    public void Evaluate_ForAPostingWithoutComp_RaisesTheUnknownCompFlag()
    {
        Assert.Contains(JobFlag.CU, prefilter.Evaluate(Input()).Flags);
    }

    [Fact]
    public void Evaluate_ForAPostingWithComp_DoesNotRaiseTheUnknownCompFlag()
    {
        Assert.DoesNotContain(JobFlag.CU, prefilter.Evaluate(Input(hasStatedComp: true)).Flags);
    }

    [Fact]
    public void Evaluate_ForARemoteRoleWhoseOnlyGeographyIsAUnitedStatesCountryCode_PassesWithTheHoursFlag()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(location: "Remote", countryIso: "US", isRemote: true));

        Assert.Equal(PrefilterState.Passed, verdict.State);
        Assert.Contains(JobFlag.H3, verdict.Flags);
    }

    [Fact]
    public void Evaluate_ForARemoteRoleWhoseOnlyGeographyIsAEuropeanCountryCode_PassesWithoutTheHoursFlag()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(location: "Remote", countryIso: "DE", isRemote: true));

        Assert.Equal(PrefilterState.Passed, verdict.State);
        Assert.DoesNotContain(JobFlag.H3, verdict.Flags);
    }

    [Fact]
    public void Evaluate_ForARemoteRoleInACountryOutsideEurope_PassesWithTheHoursFlag()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(location: "Remote", countryIso: "IN", isRemote: true));

        Assert.Equal(PrefilterState.Passed, verdict.State);
        Assert.Contains(JobFlag.H3, verdict.Flags);
    }

    [Fact]
    public void Evaluate_ForARoleThatAsksForUnitedStatesHours_RaisesTheHoursFlag()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(
            location: "Remote, Worldwide",
            isRemote: true,
            description: "The team works 9 to 5 EST and you are expected to overlap."));

        Assert.Contains(JobFlag.H3, verdict.Flags);
    }

    [Fact]
    public void Evaluate_ForAPostingThatMentionsAStackKeyword_RaisesTheStackMatchFlag()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(location: "Remote, Europe", isRemote: true));

        Assert.Contains(JobFlag.StackMatch, verdict.Flags);
    }

    [Fact]
    public void Evaluate_ForAPostingWhoseTagsAloneMentionAStackKeyword_RaisesTheStackMatchFlag()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(description: "Backend services on Kubernetes.", location: "Remote, Europe", isRemote: true, tags: ["kotlin"]));

        Assert.Contains(JobFlag.StackMatch, verdict.Flags);
    }

    [Fact]
    public void Evaluate_ForAPostingThatNeverMentionsAStackKeyword_LeavesTheStackMatchFlagOff()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(description: "We write Go and Python on Kubernetes.", location: "Remote, Europe", isRemote: true, tags: ["golang"]));

        Assert.DoesNotContain(JobFlag.StackMatch, verdict.Flags);
    }

    [Fact]
    public void FromJob_ForAStoredJob_CarriesEverythingTheRulesRead()
    {
        Job job = Job.Create("fingerprint", "https://jobs.example.com/a", "https://jobs.example.com/a", "Acme", "Senior Backend Engineer", "Remote role.", "hash", EvaluatedAt, isManual: false);
        job.RecordSource(JobSourceKind.RemoteOk, "1", EvaluatedAt);
        job.RecordPostingFacts(null, null, null, ["kotlin"], null, null);
        job.RecordPlace("Remote, Europe", null, "Europe", true, "en");
        job.RecordCompensation(90_000m, 120_000m, "EUR", CompPeriod.Year, 90_000m, 120_000m);
        JobHunter.Domain.Settings settings = JobHunter.Domain.Settings.CreateDefault();

        PrefilterInput input = PrefilterInput.FromJob(job, CandidateProfiles.NetherlandsJava, settings, EvaluatedAt);

        Assert.True(input.HasStatedComp);
        Assert.Equal<JobSourceKind>([JobSourceKind.RemoteOk], input.Sources);
        Assert.Equal<string>(["kotlin"], input.Tags);
        Assert.Equal("Remote, Europe", input.LocationText);
        Assert.Same(CandidateProfiles.NetherlandsJava, input.Candidate);
        Assert.True(input.KeepOnsiteWithCompOrRelocation);
        Assert.Equal(PrefilterState.Passed, prefilter.Evaluate(input).State);
    }

    [Fact]
    public void FromJob_ForTheSettingsAlone_BuildsTheCandidateProfileFromThem()
    {
        Job job = Job.Create("fingerprint", "https://jobs.example.com/a", "https://jobs.example.com/a", "Acme", "Senior Backend Engineer", "Remote role.", "hash", EvaluatedAt, isManual: false);
        JobHunter.Domain.Settings settings = JobHunter.Domain.Settings.CreateDefault();
        settings.ConfigureCandidate("DE", "Berlin", true, false, "en,de", "EUR", "Java", ContractPreference.Either, false, null, "engineer", "manager");

        PrefilterInput input = PrefilterInput.FromJob(job, settings, EvaluatedAt);

        Assert.Equal("DE", input.Candidate.HomeCountryIso);
        Assert.Equal("Berlin", input.Candidate.HomeCity);
        Assert.False(input.Candidate.AcceptsUnitedStatesRemote);
        Assert.Equal<string>(["de", "en"], input.Candidate.AcceptedLanguages.Order());
        Assert.Equal<string>(["Java"], input.Candidate.StackKeywords.Keywords);
    }

    [Fact]
    public void Evaluate_ForBerlinAndAnOnsiteRoleInBerlin_PassesWithTheRelocationAndHomeCityFlags()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(location: "Berlin, Germany", countryIso: "DE", isRemote: false, candidate: CandidateProfiles.Berlin));

        Assert.Equal(PrefilterState.Passed, verdict.State);
        Assert.Contains(JobFlag.H4, verdict.Flags);
        Assert.Contains(JobFlag.HomeCity, verdict.Flags);
        Assert.DoesNotContain(JobFlag.H3, verdict.Flags);
    }

    [Fact]
    public void Evaluate_ForBerlinAndAHybridRoleInAnotherCityOfTheHomeCountry_PassesWithoutTheHomeCityFlag()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(location: "Hamburg, Germany - Hybrid", countryIso: "DE", candidate: CandidateProfiles.Berlin));

        Assert.Equal(PrefilterState.Passed, verdict.State);
        Assert.Contains(JobFlag.H4, verdict.Flags);
        Assert.DoesNotContain(JobFlag.HomeCity, verdict.Flags);
    }

    [Fact]
    public void Evaluate_ForAHybridRoleInAnotherCityOfTheHomeCountry_PassesWithoutTheHomeCityFlag()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(location: "Amsterdam, Netherlands - Hybrid", countryIso: "NL"));

        Assert.Equal(PrefilterState.Passed, verdict.State);
        Assert.Contains(JobFlag.H4, verdict.Flags);
        Assert.DoesNotContain(JobFlag.HomeCity, verdict.Flags);
    }

    [Fact]
    public void Evaluate_ForAHybridRoleThatNamesOnlyTheHomeCity_KeepsItAsHomeWithoutACountry()
    {
        CandidateProfile candidate = new("PL", "Poznan", acceptsEuropeRemote: false, acceptsUnitedStatesRemote: false, ["en"], [], [], []);

        PrefilterVerdict verdict = prefilter.Evaluate(Input(location: "Poznań - Hybrid", candidate: candidate));

        Assert.Equal(PrefilterState.Passed, verdict.State);
        Assert.Contains(JobFlag.H4, verdict.Flags);
        Assert.Contains(JobFlag.HomeCity, verdict.Flags);
    }

    [Theory]
    [InlineData("Poznan", "Poznań, Poland", true)]
    [InlineData("Poznań", "Poznan - Hybrid", true)]
    [InlineData("utrecht", "UTRECHT, NL", true)]
    [InlineData(" Utrecht ", "Hybrid in Utrecht", true)]
    [InlineData("Bern", "Bernau bei Berlin", false)]
    [InlineData("Utrecht", "Amsterdam, Netherlands", false)]
    [InlineData("", "Utrecht", false)]
    [InlineData("Utrecht", null, false)]
    public void IsInHomeCity_ForAPlaceText_MatchesTheCityAsAWholeWordInAnyCaseWithOrWithoutDiacritics(string homeCity, string? placeText, bool expected)
    {
        Assert.Equal(expected, GeographyRules.IsInHomeCity(homeCity, placeText));
    }

    [Theory]
    [InlineData("Remote, Romania")]
    [InlineData("Remote, România")]
    [InlineData("Remote - Bucharest")]
    [InlineData("Remote - Cluj-Napoca")]
    [InlineData("Remote - Cluj")]
    public void Evaluate_ForAUnitedStatesCandidateAndARemoteRoleInAEuropeanPlace_DropsItAsEuropeOnly(string location)
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(location: location, isRemote: true, candidate: CandidateProfiles.UnitedStatesOnly));

        Assert.Equal(PrefilterState.Dropped, verdict.State);
        Assert.Equal(GeographyRules.EuropeOnlyReason, verdict.DropReason);
    }

    [Fact]
    public void Evaluate_ForBerlinAndAHybridRoleInUtrecht_PassesWithoutTheHomeCityFlag()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(location: "Utrecht, Netherlands - Hybrid", countryIso: "NL", candidate: CandidateProfiles.Berlin));

        Assert.Equal(PrefilterState.Passed, verdict.State);
        Assert.Contains(JobFlag.H4, verdict.Flags);
        Assert.DoesNotContain(JobFlag.HomeCity, verdict.Flags);
    }

    [Fact]
    public void Evaluate_ForBerlinAndARemoteRoleOpenToTheUnitedStatesOnly_Drops()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(location: "Remote (US only)", isRemote: true, candidate: CandidateProfiles.Berlin));

        Assert.Equal(PrefilterState.Dropped, verdict.State);
        Assert.Equal(GeographyRules.UnitedStatesOnlyReason, verdict.DropReason);
    }

    [Fact]
    public void Evaluate_ForBerlinAndARemoteEuropeanRole_PassesWithoutTheHoursFlag()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(location: "Remote, Europe", isRemote: true, candidate: CandidateProfiles.Berlin));

        Assert.Equal(PrefilterState.Passed, verdict.State);
        Assert.DoesNotContain(JobFlag.H3, verdict.Flags);
    }

    [Fact]
    public void Evaluate_ForBerlinAndAnOnsiteRoleInAustinWithoutCompOrRelocation_Drops()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(location: "Austin, TX, United States", countryIso: "US", isRemote: false, candidate: CandidateProfiles.Berlin));

        Assert.Equal(PrefilterState.Dropped, verdict.State);
        Assert.Equal(GeographyRules.OnsiteWithoutBasisReason, verdict.DropReason);
    }

    [Theory]
    [InlineData("nl")]
    [InlineData("de")]
    public void Evaluate_ForBerlinAndAPostingInALanguageOtherThanEnglish_Drops(string language)
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(language: language, candidate: CandidateProfiles.Berlin));

        Assert.Equal(Prefilter.LanguageReason, verdict.DropReason);
    }

    [Fact]
    public void Evaluate_ForBerlinAndAJavaTitle_RaisesTheStackMatchFlag()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(title: "Senior Java Engineer", description: "Backend services on Kubernetes.", candidate: CandidateProfiles.Berlin));

        Assert.Equal(PrefilterState.Passed, verdict.State);
        Assert.Contains(JobFlag.StackMatch, verdict.Flags);
    }

    [Fact]
    public void Evaluate_ForBerlinAndAPostingInAnotherStack_LeavesTheStackMatchFlagOff()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(title: "Senior .NET Engineer", description: "We run C# and .NET on Azure; JavaScript on the front.", tags: ["c#", ".NET"], candidate: CandidateProfiles.Berlin));

        Assert.Equal(PrefilterState.Passed, verdict.State);
        Assert.DoesNotContain(JobFlag.StackMatch, verdict.Flags);
    }

    [Fact]
    public void Evaluate_ForBerlinAndAnExcludedTitle_DropsWithTheTermThatCaughtIt()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(title: "Senior Frontend Engineer", candidate: CandidateProfiles.Berlin));

        Assert.Equal("title excluded: frontend", verdict.DropReason);
    }

    [Fact]
    public void Evaluate_ForAUnitedStatesCandidateAndARoleThatAsksForCentralEuropeanHours_RaisesTheHoursFlag()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(
            location: "Remote, Worldwide",
            isRemote: true,
            description: "You overlap at least four hours with CET.",
            candidate: CandidateProfiles.UnitedStatesOnly));

        Assert.Equal(PrefilterState.Passed, verdict.State);
        Assert.Contains(JobFlag.H3, verdict.Flags);
    }

    [Fact]
    public void Evaluate_ForAUnitedStatesCandidateAndARoleThatAsksForUnitedStatesHours_LeavesTheHoursFlagOff()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(
            location: "Remote, Worldwide",
            isRemote: true,
            description: "The team works 9 to 5 EST and you are expected to overlap.",
            candidate: CandidateProfiles.UnitedStatesOnly));

        Assert.DoesNotContain(JobFlag.H3, verdict.Flags);
    }

    [Fact]
    public void Evaluate_ForAUnitedStatesCandidateAndARemoteRoleOpenToTheUnitedStatesOnly_PassesWithoutTheHoursFlag()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(location: "Remote (US only)", countryIso: "US", isRemote: true, candidate: CandidateProfiles.UnitedStatesOnly));

        Assert.Equal(PrefilterState.Passed, verdict.State);
        Assert.DoesNotContain(JobFlag.H3, verdict.Flags);
    }

    [Fact]
    public void Evaluate_ForAUnitedStatesCandidateAndARemoteRoleOpenToEuropeOnly_Drops()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(location: "Remote (Europe only)", isRemote: true, candidate: CandidateProfiles.UnitedStatesOnly));

        Assert.Equal(PrefilterState.Dropped, verdict.State);
        Assert.Equal(GeographyRules.EuropeOnlyReason, verdict.DropReason);
    }

    [Fact]
    public void Evaluate_ForAUnitedStatesCandidateAndARemoteRoleInAEuropeanCountry_RaisesTheHoursFlag()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(location: "Remote", countryIso: "DE", isRemote: true, candidate: CandidateProfiles.UnitedStatesOnly));

        Assert.Contains(JobFlag.H3, verdict.Flags);
    }

    [Fact]
    public void Evaluate_ForAUnitedStatesCandidateAndAnOnsiteRoleInAustin_PassesWithTheHomeCityFlag()
    {
        PrefilterVerdict verdict = prefilter.Evaluate(Input(location: "Austin, TX, United States", countryIso: "US", isRemote: false, candidate: CandidateProfiles.UnitedStatesOnly));

        Assert.Equal(PrefilterState.Passed, verdict.State);
        Assert.Contains(JobFlag.H4, verdict.Flags);
        Assert.Contains(JobFlag.HomeCity, verdict.Flags);
        Assert.DoesNotContain(JobFlag.H3, verdict.Flags);
    }

    private static PrefilterInput Input(
        string title = "Senior Backend Engineer",
        string description = "We run Kotlin on Kubernetes and pay in euro.",
        string? location = null,
        string? region = null,
        string? countryIso = null,
        bool? isRemote = null,
        string? language = "en",
        IReadOnlyList<string>? tags = null,
        JobSourceKind source = JobSourceKind.Dataset,
        bool isManual = false,
        DateTimeOffset? postedAt = null,
        bool withoutPostedDate = false,
        bool hasStatedComp = false,
        CandidateProfile? candidate = null,
        bool keepOnsiteWithCompOrRelocation = true)
    {
        return new PrefilterInput(
            title,
            description,
            location,
            region,
            countryIso,
            isRemote,
            language,
            tags ?? [],
            [source],
            isManual,
            withoutPostedDate ? null : postedAt ?? EvaluatedAt.AddDays(-3),
            hasStatedComp,
            EvaluatedAt,
            candidate ?? CandidateProfiles.NetherlandsJava,
            keepOnsiteWithCompOrRelocation);
    }
}
