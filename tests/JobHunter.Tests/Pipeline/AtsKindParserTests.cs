using JobHunter.Domain;
using JobHunter.Pipeline;

namespace JobHunter.Tests.Pipeline;

/// <summary>Proves that the applicant tracking system is recognized from what the dataset reports and from the apply link.</summary>
public sealed class AtsKindParserTests
{
    [Theory]
    [InlineData("greenhouse", AtsKind.Greenhouse)]
    [InlineData("Lever", AtsKind.Lever)]
    [InlineData("ashby", AtsKind.Ashby)]
    [InlineData("workable", AtsKind.Workable)]
    [InlineData("smartrecruiters", AtsKind.SmartRecruiters)]
    [InlineData("workday", AtsKind.Other)]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void FromName_ForWhatTheDatasetReports_ReturnsTheSystem(string? atsType, AtsKind? expected)
    {
        Assert.Equal(expected, AtsKindParser.FromName(atsType));
    }

    [Theory]
    [InlineData("https://job-boards.greenhouse.io/acme/jobs/123", AtsKind.Greenhouse)]
    [InlineData("https://boards.greenhouse.io/acme/jobs/123", AtsKind.Greenhouse)]
    [InlineData("https://jobs.lever.co/acme/42", AtsKind.Lever)]
    [InlineData("https://jobs.ashbyhq.com/acme/uuid", AtsKind.Ashby)]
    [InlineData("https://apply.workable.com/acme/j/ABC123", AtsKind.Workable)]
    [InlineData("https://jobs.smartrecruiters.com/acme/7440", AtsKind.SmartRecruiters)]
    [InlineData("https://careers.acme.com/jobs/1", null)]
    [InlineData("not a url", null)]
    [InlineData(null, null)]
    public void FromUrl_ForAnApplyLink_ReturnsTheSystemBehindIt(string? url, AtsKind? expected)
    {
        Assert.Equal(expected, AtsKindParser.FromUrl(url));
    }

    [Fact]
    public void FromUrl_ForAHostThatMerelyEndsInTheSameLetters_ReturnsNothing()
    {
        Assert.Null(AtsKindParser.FromUrl("https://notgreenhouse.io/jobs/1"));
    }

    [Fact]
    public void Parse_WhenTheDatasetNamesTheSystem_PrefersThatName()
    {
        Assert.Equal(AtsKind.Ashby, AtsKindParser.Parse("ashby", "https://careers.acme.com/jobs/1"));
    }

    [Fact]
    public void Parse_WhenOnlyTheApplyLinkIsKnown_ReadsTheLink()
    {
        Assert.Equal(AtsKind.Lever, AtsKindParser.Parse(null, "https://jobs.lever.co/acme/42"));
    }

    [Fact]
    public void Parse_WhenNeitherSaysAnything_ReturnsNothing()
    {
        Assert.Null(AtsKindParser.Parse(null, "https://careers.acme.com/jobs/1"));
    }
}
