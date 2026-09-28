using JobHunter.Domain;
using JobHunter.Jobs;

namespace JobHunter.Tests.Jobs;

/// <summary>Proves which flags count against a job and which the inbox rows show, the code, tone and meaning every chip takes from the candidate's own settings, and what an unsave names as deleted.</summary>
public sealed class JobDisplayTests
{
    private static readonly DateTimeOffset SeenAt = new(2026, 9, 14, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(TriageState.New, "New")]
    [InlineData(TriageState.Pursued, "Saved")]
    [InlineData(TriageState.Skipped, "Skipped")]
    public void TriageLabel_ForEachTriageState_ReadsAsThePagesNameIt(TriageState triage, string expected)
    {
        Assert.Equal(expected, JobDisplay.TriageLabel(triage));
    }

    [Theory]
    [InlineData(JobFlag.H3)]
    [InlineData(JobFlag.H4)]
    [InlineData(JobFlag.WA)]
    public void CountsAgainst_ForAFlagThatAlwaysWeighsAgainstTheJob_ReturnsTrue(JobFlag flag)
    {
        Assert.True(JobDisplay.CountsAgainst(flag, Settings()));
    }

    [Theory]
    [InlineData(JobFlag.H1)]
    [InlineData(JobFlag.CU)]
    [InlineData(JobFlag.HomeCity)]
    public void CountsAgainst_ForAFlagTheRowAlreadySays_ReturnsFalse(JobFlag flag)
    {
        Assert.False(JobDisplay.CountsAgainst(flag, Settings()));
    }

    [Fact]
    public void CountsAgainst_ForEmploymentOnly_ReturnsTrueOnlyForAContractor()
    {
        Assert.True(JobDisplay.CountsAgainst(JobFlag.H2, Settings(contractPreference: ContractPreference.Contractor)));
        Assert.False(JobDisplay.CountsAgainst(JobFlag.H2, Settings(contractPreference: ContractPreference.Employee)));
        Assert.False(JobDisplay.CountsAgainst(JobFlag.H2, Settings(contractPreference: ContractPreference.Either)));
    }

    [Fact]
    public void FlagsShownOnInbox_ForEveryFlag_KeepsTheFlagsAgainstTheJobAndTheHighlightsInTheirStoredOrder()
    {
        JobHunter.Domain.Settings settings = Settings(contractPreference: ContractPreference.Contractor);

        IReadOnlyList<JobFlag> shown = JobDisplay.FlagsShownOnInbox([JobFlag.WA, JobFlag.H1, JobFlag.StackMatch, JobFlag.H3, JobFlag.CU, JobFlag.HomeCity, JobFlag.HighPay, JobFlag.H4, JobFlag.H2], settings);

        Assert.Equal<JobFlag>([JobFlag.WA, JobFlag.StackMatch, JobFlag.H3, JobFlag.HighPay, JobFlag.H4, JobFlag.H2], shown);
    }

    [Fact]
    public void FlagsShownOnInbox_ForOnlyFlagsTheRowAlreadySays_ReturnsNothing()
    {
        Assert.Empty(JobDisplay.FlagsShownOnInbox([JobFlag.H1, JobFlag.CU, JobFlag.HomeCity], Settings()));
    }

    [Theory]
    [InlineData(JobFlag.H1, "H1")]
    [InlineData(JobFlag.H2, "H2")]
    [InlineData(JobFlag.H3, "H3")]
    [InlineData(JobFlag.H4, "H4")]
    [InlineData(JobFlag.WA, "WA")]
    [InlineData(JobFlag.CU, "CU")]
    public void FlagCode_ForAStoredNameFlag_ReturnsItsEnumName(JobFlag flag, string expected)
    {
        Assert.Equal(expected, JobDisplay.FlagCode(flag, Settings()));
    }

    [Fact]
    public void FlagCode_ForStackMatch_ReturnsTheMatchedKeywordWhenGiven()
    {
        Assert.Equal("Kotlin", JobDisplay.FlagCode(JobFlag.StackMatch, Settings(), matchedStackKeyword: "Kotlin"));
    }

    [Fact]
    public void FlagCode_ForStackMatch_FallsBackToTheFirstConfiguredKeywordWhenNoMatchIsGiven()
    {
        Assert.Equal("Java", JobDisplay.FlagCode(JobFlag.StackMatch, Settings()));
    }

    [Fact]
    public void FlagCode_ForStackMatch_FallsBackToAGenericWordWhenNoKeywordIsConfigured()
    {
        Assert.Equal("stack", JobDisplay.FlagCode(JobFlag.StackMatch, Settings(stackKeywords: string.Empty)));
    }

    [Fact]
    public void FlagCode_ForHomeCity_ReturnsTheConfiguredCity()
    {
        Assert.Equal("Utrecht", JobDisplay.FlagCode(JobFlag.HomeCity, Settings(homeCity: "Utrecht")));
    }

    [Fact]
    public void FlagCode_ForHomeCity_FallsBackToAGenericWordWhenNoneIsConfigured()
    {
        Assert.Equal("home", JobDisplay.FlagCode(JobFlag.HomeCity, Settings(homeCity: string.Empty)));
    }

    [Theory]
    [InlineData(110_000, "110k+")]
    [InlineData(125_000, "125k+")]
    [InlineData(500, "500+")]
    public void FlagCode_ForHighPay_ShowsTheThresholdInThousandsOrWholeUnderAThousand(decimal threshold, string expected)
    {
        Assert.Equal(expected, JobDisplay.FlagCode(JobFlag.HighPay, Settings(highPayThresholdPerYear: threshold)));
    }

    [Fact]
    public void FlagCode_ForHighPay_FallsBackToAGenericWordWhenNoThresholdIsConfigured()
    {
        Assert.Equal("high pay", JobDisplay.FlagCode(JobFlag.HighPay, Settings(highPayThresholdPerYear: null)));
    }

    [Theory]
    [InlineData(JobFlag.StackMatch, "stack-match")]
    [InlineData(JobFlag.HighPay, "high-pay")]
    [InlineData(JobFlag.H1, "good")]
    [InlineData(JobFlag.HomeCity, "good")]
    [InlineData(JobFlag.H3, "warn")]
    [InlineData(JobFlag.H4, "warn")]
    [InlineData(JobFlag.WA, "bad")]
    [InlineData(JobFlag.CU, null)]
    public void FlagTone_ForEachFlag_ReturnsTheModifierOfItsMeaning(JobFlag flag, string? expected)
    {
        Assert.Equal(expected, JobDisplay.FlagTone(flag, Settings()));
    }

    [Fact]
    public void FlagTone_ForEmploymentOnly_IsWarnOnlyWhenItCountsAgainstTheJob()
    {
        Assert.Equal("warn", JobDisplay.FlagTone(JobFlag.H2, Settings(contractPreference: ContractPreference.Contractor)));
        Assert.Null(JobDisplay.FlagTone(JobFlag.H2, Settings(contractPreference: ContractPreference.Employee)));
    }

    [Fact]
    public void FlagMeaning_ForStackMatch_MentionsTheCandidatesStackKeywords()
    {
        Assert.Equal("mentions one of your stack keywords", JobDisplay.FlagMeaning(JobFlag.StackMatch, Settings()));
    }

    [Fact]
    public void FlagMeaning_ForHomeCity_SaysInYourHomeCity()
    {
        Assert.Equal("in your home city", JobDisplay.FlagMeaning(JobFlag.HomeCity, Settings()));
    }

    [Fact]
    public void FlagMeaning_ForNonEuropeanHours_SaysOutsideYourAcceptedRegions()
    {
        Assert.Equal("outside your accepted regions or their working hours", JobDisplay.FlagMeaning(JobFlag.H3, Settings()));
    }

    [Fact]
    public void FlagMeaning_ForHighPay_StatesTheThresholdInTheBaseCurrency()
    {
        Assert.Equal("pays at least 110,000 EUR a year", JobDisplay.FlagMeaning(JobFlag.HighPay, Settings(highPayThresholdPerYear: 110_000m, baseCurrency: "EUR")));
    }

    [Fact]
    public void FlagMeaning_ForHighPay_FallsBackWhenNoThresholdIsConfigured()
    {
        Assert.Equal("pays at least your configured high-pay threshold a year", JobDisplay.FlagMeaning(JobFlag.HighPay, Settings(highPayThresholdPerYear: null)));
    }

    [Theory]
    [InlineData(JobFlag.H3, "outside your regions")]
    [InlineData(JobFlag.WA, "US authorization")]
    [InlineData(JobFlag.CU, "pay not stated")]
    [InlineData(JobFlag.StackMatch, null)]
    [InlineData(JobFlag.HighPay, null)]
    [InlineData(JobFlag.HomeCity, null)]
    public void FlagLabel_ForEachFlag_ReturnsTheShortWordOrNothing(JobFlag flag, string? expected)
    {
        Assert.Equal(expected, JobDisplay.FlagLabel(flag));
    }

    [Fact]
    public void Comp_ForAStatedRange_ShowsItInTheBaseCurrency()
    {
        Assert.Equal("≈ 90,000 - 120,000 USD/year", JobDisplay.Comp(90_000m, 120_000m, "USD"));
    }

    [Fact]
    public void Place_ForAKnownPolicyAndLocation_JoinsThemWithAComma()
    {
        Assert.Equal("remote, Utrecht", JobDisplay.Place("remote", "Utrecht", null));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("Unknown")]
    [InlineData("UNKNOWN")]
    [InlineData(" unknown ")]
    public void Place_ForAnUnknownPolicyWithALocation_ShowsOnlyThePlace(string remotePolicy)
    {
        Assert.Equal("Utrecht", JobDisplay.Place(remotePolicy, "Utrecht", null));
    }

    [Fact]
    public void Place_ForAnUnknownPolicyAndNoLocation_ShowsUnknown()
    {
        Assert.Equal("unknown", JobDisplay.Place("unknown", null, null));
    }

    [Fact]
    public void Place_ForNoPolicyAndNoLocation_ShowsUnknown()
    {
        Assert.Equal("unknown", JobDisplay.Place(null, null, null));
    }

    [Fact]
    public void Place_ForAKnownPolicyAndNoLocation_ShowsOnlyThePolicy()
    {
        Assert.Equal("remote", JobDisplay.Place("remote", null, null));
    }

    [Fact]
    public void Place_ForNoPolicyAndACountryIsoFallback_ShowsOnlyThePlace()
    {
        Assert.Equal("Netherlands", JobDisplay.Place(null, null, "Netherlands"));
    }

    [Fact]
    public void UnpursueLosses_ForAnApplicationWithACoverLetter_NamesTheLetterAfterTheKit()
    {
        Application application = Application.Create(Guid.NewGuid(), ApplicationStatus.Saved, SeenAt, "Saved from the inbox.");
        application.AttachKit(new ApplicationKit(["A fact."], "A cover note.", ["A question?"], "resume.pdf", "en", SeenAt, "claude-opus-5", []));
        application.AttachCoverLetter(new ApplicationCoverLetter("en", "Dear Example Co team,", ["One.", "Two.", "Three."], "Kind regards,", SeenAt, "claude-code", []));

        Assert.Equal("the application, its kit, its cover letter and its status history", JobDisplay.UnpursueLosses(application));
    }

    [Fact]
    public void UnpursueLosses_ForAnApplicationWithoutACoverLetter_LeavesTheLetterOut()
    {
        Application application = Application.Create(Guid.NewGuid(), ApplicationStatus.Saved, SeenAt, "Saved from the inbox.");
        application.AddNote("Asked a friend about the team.", SeenAt);

        Assert.Equal("the application, its note and its status history", JobDisplay.UnpursueLosses(application));
    }

    [Fact]
    public void UnpursueLosses_ForAnApplicationWithOnlyACoverLetter_NamesTheLetter()
    {
        Application application = Application.Create(Guid.NewGuid(), ApplicationStatus.Saved, SeenAt, "Saved from the inbox.");
        application.AttachCoverLetter(new ApplicationCoverLetter("en", "Dear Example Co team,", ["One.", "Two.", "Three."], "Kind regards,", SeenAt, "claude-code", []));

        Assert.Contains("its cover letter", JobDisplay.UnpursueLosses(application), StringComparison.Ordinal);
        Assert.DoesNotContain("its kit", JobDisplay.UnpursueLosses(application), StringComparison.Ordinal);
    }

    private static JobHunter.Domain.Settings Settings(
        string? homeCountryIso = "NL",
        string homeCity = "Utrecht",
        string stackKeywords = "Java, Kotlin",
        decimal? highPayThresholdPerYear = 110_000m,
        string baseCurrency = "EUR",
        ContractPreference contractPreference = ContractPreference.Either)
    {
        JobHunter.Domain.Settings settings = JobHunter.Domain.Settings.CreateDefault();
        settings.ConfigureCandidate(
            homeCountryIso,
            homeCity,
            true,
            true,
            "en",
            baseCurrency,
            stackKeywords,
            contractPreference,
            hasUnitedStatesWorkAuthorization: false,
            highPayThresholdPerYear,
            JobHunter.Domain.Settings.DefaultTitleIncludeTerms,
            JobHunter.Domain.Settings.DefaultTitleExcludeTerms);

        return settings;
    }
}
