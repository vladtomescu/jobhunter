using JobHunter.Pipeline;

namespace JobHunter.Tests.Pipeline;

/// <summary>Proves that the same posting reached through two feeds is recognized once, and that two different openings stay apart.</summary>
public sealed class DuplicateMatcherTests
{
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

        DuplicateCandidate? match = DuplicateMatcher.FindMatch([known], "Acme", "Senior Backend Engineer (Remote)", FirstSeen.AddDays(10));

        Assert.Equal(known, match);
    }

    [Fact]
    public void FindMatch_WhenTheWindowHasPassed_ReturnsNothing()
    {
        DuplicateCandidate known = Candidate("Acme Inc.", "Senior Backend Engineer");

        Assert.Null(DuplicateMatcher.FindMatch([known], "Acme", "Senior Backend Engineer", FirstSeen.AddDays(31)));
    }

    [Fact]
    public void FindMatch_OnTheLastDayOfTheWindow_ReturnsTheKnownJob()
    {
        DuplicateCandidate known = Candidate("Acme Inc.", "Senior Backend Engineer");

        Assert.NotNull(DuplicateMatcher.FindMatch([known], "Acme", "Senior Backend Engineer", FirstSeen.AddDays(30)));
    }

    [Fact]
    public void FindMatch_ForTheSameTitleAtAnotherCompany_ReturnsNothing()
    {
        DuplicateCandidate known = Candidate("Acme Inc.", "Senior Backend Engineer");

        Assert.Null(DuplicateMatcher.FindMatch([known], "Globex", "Senior Backend Engineer", FirstSeen.AddDays(1)));
    }

    [Fact]
    public void FindMatch_ForAnotherOpeningAtTheSameCompany_ReturnsNothing()
    {
        DuplicateCandidate known = Candidate("Acme Inc.", "Senior Backend Engineer");

        Assert.Null(DuplicateMatcher.FindMatch([known], "Acme", "Senior Data Scientist", FirstSeen.AddDays(1)));
    }

    [Fact]
    public void FindMatch_WithSeveralKnownJobs_ReturnsTheClosestTitle()
    {
        DuplicateCandidate exact = Candidate("Acme", "Staff Platform Engineer");
        DuplicateCandidate other = Candidate("Acme", "Staff Platform Engineer II");

        DuplicateCandidate? match = DuplicateMatcher.FindMatch([other, exact], "Acme", "Staff Platform Engineer", FirstSeen.AddDays(2));

        Assert.Equal(exact, match);
    }

    private static DuplicateCandidate Candidate(string company, string title)
    {
        return new DuplicateCandidate(Guid.CreateVersion7(), company, title, FirstSeen);
    }
}
