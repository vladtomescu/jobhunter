using JobHunter.Domain;
using JobHunter.Jobs;

namespace JobHunter.Tests.Jobs;

/// <summary>Proves which flags count against a job and which the inbox rows show, and the code, tone and meaning every chip takes from the candidate's own settings.</summary>
public sealed class JobDisplayTests
{
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
    [InlineData(JobFlag.HomeCountry)]
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

        IReadOnlyList<JobFlag> shown = JobDisplay.FlagsShownOnInbox([JobFlag.WA, JobFlag.H1, JobFlag.StackMatch, JobFlag.H3, JobFlag.CU, JobFlag.HomeCountry, JobFlag.HighPay, JobFlag.H4, JobFlag.H2], settings);

        Assert.Equal<JobFlag>([JobFlag.WA, JobFlag.StackMatch, JobFlag.H3, JobFlag.HighPay, JobFlag.H4, JobFlag.H2], shown);
    }

    [Fact]
    public void FlagsShownOnInbox_ForOnlyFlagsTheRowAlreadySays_ReturnsNothing()
    {
        Assert.Empty(JobDisplay.FlagsShownOnInbox([JobFlag.H1, JobFlag.CU, JobFlag.HomeCountry], Settings()));
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
    public void FlagCode_ForHomeCountry_ReturnsTheConfiguredIsoCode()
    {
        Assert.Equal("DE", JobDisplay.FlagCode(JobFlag.HomeCountry, Settings(homeCountryIso: "DE")));
    }

    [Fact]
    public void FlagCode_ForHomeCountry_FallsBackToAGenericWordWhenNoneIsConfigured()
    {
        Assert.Equal("home", JobDisplay.FlagCode(JobFlag.HomeCountry, Settings(homeCountryIso: null)));
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
    [InlineData(JobFlag.HomeCountry, "good")]
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
    public void FlagMeaning_ForHomeCountry_SaysInYourHomeCountry()
    {
        Assert.Equal("in your home country", JobDisplay.FlagMeaning(JobFlag.HomeCountry, Settings()));
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
    [InlineData(JobFlag.HomeCountry, null)]
    public void FlagLabel_ForEachFlag_ReturnsTheShortWordOrNothing(JobFlag flag, string? expected)
    {
        Assert.Equal(expected, JobDisplay.FlagLabel(flag));
    }

    [Fact]
    public void Comp_ForAStatedRange_ShowsItInTheBaseCurrency()
    {
        Assert.Equal("≈ 90,000 - 120,000 USD/year", JobDisplay.Comp(90_000m, 120_000m, "USD"));
    }

    private static JobHunter.Domain.Settings Settings(
        string? homeCountryIso = "NL",
        string stackKeywords = "Java, Kotlin",
        decimal? highPayThresholdPerYear = 110_000m,
        string baseCurrency = "EUR",
        ContractPreference contractPreference = ContractPreference.Either)
    {
        JobHunter.Domain.Settings settings = JobHunter.Domain.Settings.CreateDefault();
        settings.ConfigureCandidate(
            homeCountryIso,
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
