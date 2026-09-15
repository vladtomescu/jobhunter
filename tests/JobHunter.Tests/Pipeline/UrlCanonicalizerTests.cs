using JobHunter.Pipeline;

namespace JobHunter.Tests.Pipeline;

/// <summary>Proves that two sightings of the same posting reduce to the same URL, whatever the campaign parameters say.</summary>
public sealed class UrlCanonicalizerTests
{
    [Theory]
    [InlineData("https://JOBS.Example.com/backend", "https://jobs.example.com/backend")]
    [InlineData("HTTPS://Boards.Greenhouse.IO/acme/jobs/123", "https://boards.greenhouse.io/acme/jobs/123")]
    [InlineData("https://jobs.example.com/backend?utm_source=remoteok&utm_medium=feed", "https://jobs.example.com/backend")]
    [InlineData("https://jobs.example.com/backend?UTM_Campaign=spring", "https://jobs.example.com/backend")]
    [InlineData("https://jobs.example.com/backend?ref=weworkremotely", "https://jobs.example.com/backend")]
    [InlineData("https://boards.greenhouse.io/acme/jobs/123?gh_src=abcdef", "https://boards.greenhouse.io/acme/jobs/123")]
    [InlineData("https://jobs.lever.co/acme/42?lever-source=LinkedIn", "https://jobs.lever.co/acme/42")]
    [InlineData("https://jobs.example.com/backend?source=indeed", "https://jobs.example.com/backend")]
    [InlineData("https://jobs.example.com/backend#application", "https://jobs.example.com/backend")]
    [InlineData("https://jobs.example.com/backend?gh_jid=77&utm_source=x#apply", "https://jobs.example.com/backend?gh_jid=77")]
    [InlineData("https://jobs.example.com/backend?gh_jid=77&ref=x&lever-source=y", "https://jobs.example.com/backend?gh_jid=77")]
    [InlineData("https://jobs.example.com/backend/", "https://jobs.example.com/backend")]
    [InlineData("  https://jobs.example.com/backend  ", "https://jobs.example.com/backend")]
    [InlineData("https://jobs.example.com:443/backend", "https://jobs.example.com/backend")]
    public void Canonicalize_ForAPostingUrl_KeepsOnlyWhatIdentifiesTheJob(string url, string expected)
    {
        Assert.Equal(expected, UrlCanonicalizer.Canonicalize(url));
    }

    [Theory]
    [InlineData("https://jobs.example.com/a?q=Backend%20Engineer", "https://jobs.example.com/a?q=Backend%20Engineer")]
    [InlineData("https://jobs.example.com/a?id=1&sort=date", "https://jobs.example.com/a?id=1&sort=date")]
    public void Canonicalize_ForParametersThatSelectTheJob_KeepsThem(string url, string expected)
    {
        Assert.Equal(expected, UrlCanonicalizer.Canonicalize(url));
    }

    [Fact]
    public void Canonicalize_ForSomethingThatIsNotAUrl_ReturnsItTrimmed()
    {
        Assert.Equal("acme/jobs/42", UrlCanonicalizer.Canonicalize("  acme/jobs/42  "));
    }

    [Theory]
    [InlineData("utm_source")]
    [InlineData("UTM_Medium")]
    [InlineData("ref")]
    [InlineData("gh_src")]
    [InlineData("lever-source")]
    [InlineData("source")]
    public void IsTrackingParameter_ForACampaignParameter_IsTrue(string name)
    {
        Assert.True(UrlCanonicalizer.IsTrackingParameter(name));
    }

    [Theory]
    [InlineData("gh_jid")]
    [InlineData("id")]
    [InlineData("q")]
    public void IsTrackingParameter_ForAParameterThatSelectsTheJob_IsFalse(string name)
    {
        Assert.False(UrlCanonicalizer.IsTrackingParameter(name));
    }
}
