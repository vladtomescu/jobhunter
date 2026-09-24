using JobHunter.Domain;
using JobHunter.Jobs;

namespace JobHunter.Tests.Jobs;

/// <summary>Proves which flags count against a job, since those are the only flags the inbox rows show.</summary>
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
    public void FlagsAgainst_ForEveryFlag_KeepsOnlyTheFlagsAgainstTheJobInTheirStoredOrder()
    {
        IReadOnlyList<JobFlag> shown = JobDisplay.FlagsAgainst([JobFlag.WA, JobFlag.H1, JobFlag.H3, JobFlag.CU, JobFlag.H4, JobFlag.H2]);

        Assert.Equal<JobFlag>([JobFlag.WA, JobFlag.H3, JobFlag.H4, JobFlag.H2], shown);
    }

    [Fact]
    public void FlagsAgainst_ForOnlyNeutralFlags_ReturnsNothing()
    {
        Assert.Empty(JobDisplay.FlagsAgainst([JobFlag.H1, JobFlag.CU]));
    }
}
