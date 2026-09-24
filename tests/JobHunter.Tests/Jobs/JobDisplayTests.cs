using JobHunter.Domain;
using JobHunter.Jobs;

namespace JobHunter.Tests.Jobs;

/// <summary>Proves which flags count against a job and which the inbox rows show, and the code, tone and meaning every chip takes.</summary>
public sealed class JobDisplayTests
{
    [Theory]
    [InlineData(JobFlag.H2)]
    [InlineData(JobFlag.H3)]
    [InlineData(JobFlag.H4)]
    [InlineData(JobFlag.WA)]
    public void CountsAgainst_ForAFlagThatWeighsAgainstTheJob_ReturnsTrue(JobFlag flag)
    {
        Assert.True(JobDisplay.CountsAgainst(flag));
    }

    [Theory]
    [InlineData(JobFlag.H1)]
    [InlineData(JobFlag.CU)]
    public void CountsAgainst_ForAFlagTheRowAlreadySays_ReturnsFalse(JobFlag flag)
    {
        Assert.False(JobDisplay.CountsAgainst(flag));
    }

    [Fact]
    public void FlagsShownOnInbox_ForEveryFlag_KeepsTheFlagsAgainstTheJobAndTheHighlightsInTheirStoredOrder()
    {
        IReadOnlyList<JobFlag> shown = JobDisplay.FlagsShownOnInbox([JobFlag.WA, JobFlag.H1, JobFlag.H3, JobFlag.CU, JobFlag.HighPay, JobFlag.H4, JobFlag.H2]);

        Assert.Equal<JobFlag>([JobFlag.WA, JobFlag.H3, JobFlag.HighPay, JobFlag.H4, JobFlag.H2], shown);
    }

    [Fact]
    public void FlagsShownOnInbox_ForOnlyFlagsTheRowAlreadySays_ReturnsNothing()
    {
        Assert.Empty(JobDisplay.FlagsShownOnInbox([JobFlag.H1, JobFlag.CU]));
    }

    [Theory]
    [InlineData(JobFlag.HighPay, "110k+")]
    [InlineData(JobFlag.H1, "H1")]
    [InlineData(JobFlag.H2, "H2")]
    [InlineData(JobFlag.H3, "H3")]
    [InlineData(JobFlag.H4, "H4")]
    [InlineData(JobFlag.WA, "WA")]
    [InlineData(JobFlag.CU, "CU")]
    public void FlagCode_ForEachFlag_ReturnsTheCodeTheChipShows(JobFlag flag, string expected)
    {
        Assert.Equal(expected, JobDisplay.FlagCode(flag));
    }

    [Theory]
    [InlineData(JobFlag.HighPay, "high-pay")]
    [InlineData(JobFlag.H1, "good")]
    [InlineData(JobFlag.H2, "warn")]
    [InlineData(JobFlag.H3, "warn")]
    [InlineData(JobFlag.H4, "warn")]
    [InlineData(JobFlag.WA, "bad")]
    [InlineData(JobFlag.CU, null)]
    public void FlagTone_ForEachFlag_ReturnsTheModifierOfItsMeaning(JobFlag flag, string? expected)
    {
        Assert.Equal(expected, JobDisplay.FlagTone(flag));
    }

    [Theory]
    [InlineData(JobFlag.HighPay, "pays at least 110,000 EUR a year")]
    public void FlagMeaning_ForAHighlightFlag_SaysWhatItStandsFor(JobFlag flag, string expected)
    {
        Assert.Equal(expected, JobDisplay.FlagMeaning(flag));
    }

    [Theory]
    [InlineData(JobFlag.H3, "non-EU hours")]
    [InlineData(JobFlag.WA, "US authorization")]
    [InlineData(JobFlag.CU, "pay not stated")]
    [InlineData(JobFlag.HighPay, null)]
    public void FlagLabel_ForEachFlag_ReturnsTheShortWordOrNothing(JobFlag flag, string? expected)
    {
        Assert.Equal(expected, JobDisplay.FlagLabel(flag));
    }
}
