using JobHunter.Pipeline;
using JobHunter.Sources;

namespace JobHunter.Tests.Pipeline;

/// <summary>Proves that the same posting reached through two feeds, or copied under one, is recognized once, and that two different openings stay apart.</summary>
public sealed class DuplicateMatcherTests
{
    private const string Text = "text-hash";

    private const string OtherText = "other-text-hash";

    private static readonly DateTimeOffset FirstSeen = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Acme Inc.", "Acme")]
    [InlineData("Acme, LLC", "acme")]
    [InlineData("The Acme Company", "ACME")]
    [InlineData("Acme GmbH", "Acme")]
    public void NormalizeCompany_ForTheSameCompanyWrittenTwoWays_ReturnsOneName(string left, string right)
    {
        Assert.Equal(DuplicateMatcher.NormalizeCompany(left), DuplicateMatcher.NormalizeCompany(right));
    }

    [Fact]
    public void NormalizeCompany_ForTwoDifferentCompanies_KeepsThemApart()
    {
        Assert.NotEqual(DuplicateMatcher.NormalizeCompany("Acme"), DuplicateMatcher.NormalizeCompany("Acme Labs"));
    }

    [Theory]
    [InlineData("Senior Backend Engineer", "Senior Backend Engineer (Remote)")]
    [InlineData("Senior Backend Engineer (m/f/d)", "Senior Backend Engineer")]
    [InlineData("Senior Backend Engineer", "Senior  Backend  Engineer")]
    public void TitleSimilarity_ForTheSameOpeningWrittenTwoWays_ReachesTheThreshold(string left, string right)
    {
        Assert.True(DuplicateMatcher.TitleSimilarity(left, right) >= DuplicateMatcher.MinimumSimilarity);
    }

    [Theory]
    [InlineData("Senior Backend Engineer", "Senior Data Engineer")]
    [InlineData("Staff Platform Engineer", "Principal Security Engineer")]
    public void TitleSimilarity_ForTwoDifferentOpenings_StaysBelowTheThreshold(string left, string right)
    {
        Assert.True(DuplicateMatcher.TitleSimilarity(left, right) < DuplicateMatcher.MinimumSimilarity);
    }

    [Fact]
    public void FindMatch_ForTheSameOpeningAtTheSameCompanyInsideTheWindow_ReturnsTheKnownJob()
    {
        DuplicateCandidate known = Candidate("Acme Inc.", "Senior Backend Engineer");

        DuplicateCandidate? match = DuplicateMatcher.FindMatch([known], "Acme", "Senior Backend Engineer (Remote)", Text, JobSourceKind.Dataset, FirstSeen.AddDays(10));

        Assert.Equal(known, match);
    }

    [Fact]
    public void FindMatch_WhenTheWindowHasPassed_ReturnsNothing()
    {
        DuplicateCandidate known = Candidate("Acme Inc.", "Senior Backend Engineer");

        Assert.Null(DuplicateMatcher.FindMatch([known], "Acme", "Senior Backend Engineer", Text, JobSourceKind.Dataset, FirstSeen.AddDays(31)));
    }

    [Fact]
    public void FindMatch_OnTheLastDayOfTheWindow_ReturnsTheKnownJob()
    {
        DuplicateCandidate known = Candidate("Acme Inc.", "Senior Backend Engineer");

        Assert.NotNull(DuplicateMatcher.FindMatch([known], "Acme", "Senior Backend Engineer", Text, JobSourceKind.Dataset, FirstSeen.AddDays(30)));
    }

    [Fact]
    public void FindMatch_ForTheSameTitleAtAnotherCompany_ReturnsNothing()
    {
        DuplicateCandidate known = Candidate("Acme Inc.", "Senior Backend Engineer");

        Assert.Null(DuplicateMatcher.FindMatch([known], "Globex", "Senior Backend Engineer", Text, JobSourceKind.Dataset, FirstSeen.AddDays(1)));
    }

    [Fact]
    public void FindMatch_ForAnotherOpeningAtTheSameCompany_ReturnsNothing()
    {
        DuplicateCandidate known = Candidate("Acme Inc.", "Senior Backend Engineer");

        Assert.Null(DuplicateMatcher.FindMatch([known], "Acme", "Senior Data Scientist", Text, JobSourceKind.Dataset, FirstSeen.AddDays(1)));
    }

    [Fact]
    public void FindMatch_WithSeveralKnownJobs_ReturnsTheClosestTitle()
    {
        DuplicateCandidate exact = Candidate("Acme", "Staff Platform Engineer");
        DuplicateCandidate other = Candidate("Acme", "Staff Platform Engineer II");

        DuplicateCandidate? match = DuplicateMatcher.FindMatch([other, exact], "Acme", "Staff Platform Engineer", Text, JobSourceKind.Dataset, FirstSeen.AddDays(2));

        Assert.Equal(exact, match);
    }

    [Fact]
    public void FindMatch_ForTheSameTitleWithOtherTextFromTheSameSource_ReturnsNothing()
    {
        DuplicateCandidate known = Candidate("Acme", "Senior Backend Engineer");

        Assert.Null(DuplicateMatcher.FindMatch([known], "Acme", "Senior Backend Engineer", OtherText, JobSourceKind.Dataset, FirstSeen.AddDays(1)));
    }

    [Fact]
    public void FindMatch_ForTheSameTitleWithOtherTextFromAnotherSource_ReturnsTheKnownJob()
    {
        DuplicateCandidate known = Candidate("Acme", "Senior Backend Engineer");

        Assert.Equal(known, DuplicateMatcher.FindMatch([known], "Acme", "Senior Backend Engineer", OtherText, JobSourceKind.RemoteOk, FirstSeen.AddDays(1)));
    }

    [Fact]
    public void FindMatch_WithAKnownJobOfTheSameText_PrefersItOverACloserTitle()
    {
        DuplicateCandidate sameText = Candidate("Acme", "Senior Backend Engineer (m/f/d)");
        DuplicateCandidate otherText = new(Guid.CreateVersion7(), "Acme", "Senior Backend Engineer", FirstSeen, OtherText, [JobSourceKind.RemoteOk]);

        DuplicateCandidate? match = DuplicateMatcher.FindMatch([otherText, sameText], "Acme", "Senior Backend Engineer", Text, JobSourceKind.Dataset, FirstSeen.AddDays(1));

        Assert.Equal(sameText, match);
    }

    private static DuplicateCandidate Candidate(string company, string title)
    {
        return new DuplicateCandidate(Guid.CreateVersion7(), company, title, FirstSeen, Text, [JobSourceKind.Dataset]);
    }
}
