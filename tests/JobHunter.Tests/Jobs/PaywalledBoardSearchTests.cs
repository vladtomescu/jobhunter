using JobHunter.Jobs;

namespace JobHunter.Tests.Jobs;

/// <summary>Proves that a WeWorkRemotely or RemoteOK link is recognized and that the suggested searches encode the company and title correctly.</summary>
public sealed class PaywalledBoardSearchTests
{
    [Theory]
    [InlineData("https://weworkremotely.com/remote-jobs/acme-engineer")]
    [InlineData("https://www.weworkremotely.com/remote-jobs/acme-engineer")]
    [InlineData("https://WeWorkRemotely.com/remote-jobs/acme-engineer")]
    [InlineData("https://remoteok.com/remote-jobs/12345")]
    [InlineData("https://www.remoteok.com/remote-jobs/12345")]
    [InlineData("https://remoteok.io/remote-jobs/12345")]
    [InlineData("https://www.remoteok.io/remote-jobs/12345")]
    public void IsPaywalledBoard_ForAWeWorkRemotelyOrRemoteOkLink_ReturnsTrue(string url)
    {
        Assert.True(PaywalledBoardSearch.IsPaywalledBoard(url));
    }

    [Theory]
    [InlineData("https://job-boards.greenhouse.io/acme/jobs/123")]
    [InlineData("https://jobs.lever.co/acme/42")]
    [InlineData("https://careers.acme.com/jobs/1")]
    [InlineData("https://notremoteok.com/remote-jobs/12345")]
    [InlineData("not a url")]
    [InlineData(null)]
    [InlineData("")]
    public void IsPaywalledBoard_ForAnyOtherLink_ReturnsFalse(string? url)
    {
        Assert.False(PaywalledBoardSearch.IsPaywalledBoard(url));
    }

    [Fact]
    public void BuildSearchLinks_ForACompanyAndTitle_ReturnsTheThreeExpectedQueries()
    {
        IReadOnlyList<BoardSearchLink> links = PaywalledBoardSearch.BuildSearchLinks("Acme", "Backend Engineer");

        Assert.Equal(3, links.Count);
        Assert.Equal("\"Acme\" \"Backend Engineer\"", DecodedQuery(links[0].Url));
        Assert.Equal("\"Acme\" careers", DecodedQuery(links[1].Url));
        Assert.Equal("\"Acme\" \"Backend Engineer\" (site:greenhouse.io OR site:lever.co OR site:ashbyhq.com)", DecodedQuery(links[2].Url));
    }

    [Fact]
    public void BuildSearchLinks_ForACompanyOrTitleWithEmbeddedQuotes_StripsThem()
    {
        IReadOnlyList<BoardSearchLink> links = PaywalledBoardSearch.BuildSearchLinks("\"Acme\" Inc", "Senior \"AI\" Engineer");

        Assert.Equal("\"Acme Inc\" \"Senior AI Engineer\"", DecodedQuery(links[0].Url));
    }

    [Fact]
    public void BuildSearchLinks_ForACompanyAndTitleWithAmpersandsAndHashes_EncodesThemSoTheUrlStaysOneQueryParameter()
    {
        IReadOnlyList<BoardSearchLink> links = PaywalledBoardSearch.BuildSearchLinks("R&D #1 Labs", "C# Engineer");
        string encodedQuery = links[0].Url["https://www.google.com/search?q=".Length..];

        Assert.DoesNotContain("&", encodedQuery, StringComparison.Ordinal);
        Assert.DoesNotContain("#", encodedQuery, StringComparison.Ordinal);
        Assert.Equal("\"R&D #1 Labs\" \"C# Engineer\"", DecodedQuery(links[0].Url));
    }

    [Fact]
    public void BuildSearchLinks_ForANonAsciiCompanyOrTitle_EncodesAndDecodesBack()
    {
        IReadOnlyList<BoardSearchLink> links = PaywalledBoardSearch.BuildSearchLinks("Ítâ Café", "Ingénieur Logiciel");

        Assert.Equal("\"Ítâ Café\" \"Ingénieur Logiciel\"", DecodedQuery(links[0].Url));
    }

    [Fact]
    public void BuildSearchLinks_UsesGoogleSearchAsTheHostForEveryLink()
    {
        IReadOnlyList<BoardSearchLink> links = PaywalledBoardSearch.BuildSearchLinks("Acme", "Backend Engineer");

        Assert.All(links, link => Assert.StartsWith("https://www.google.com/search?q=", link.Url, StringComparison.Ordinal));
    }

    private static string DecodedQuery(string url)
    {
        string rawQuery = new Uri(url).Query.TrimStart('?')["q=".Length..];

        return Uri.UnescapeDataString(rawQuery);
    }
}
