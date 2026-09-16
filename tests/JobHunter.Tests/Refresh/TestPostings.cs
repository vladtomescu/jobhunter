using JobHunter.Domain;
using JobHunter.Sources;

namespace JobHunter.Tests.Refresh;

/// <summary>Builds the raw postings the refresh tests feed their fake sources.</summary>
internal static class TestPostings
{
    /// <summary>A description that trips no prefilter rule.</summary>
    public const string PlainDescription = "<p>We run a distributed platform on .NET and look after it end to end.</p>";

    /// <summary>One posting, remote and recent, with only the fields a test cares about spelled out.</summary>
    public static RawJob Posting(
        JobSourceKind source,
        string sourceId,
        string postingUrl,
        string company = "Northwind",
        string title = "Senior Backend Engineer",
        string? applyUrl = null,
        string description = PlainDescription,
        DateTimeOffset? postedAt = null,
        decimal? compMin = null,
        decimal? compMax = null,
        string? compCurrency = null,
        CompPeriod? compPeriod = null,
        string? ats = null,
        string? locationText = null,
        string? countryIso = null)
    {
        return new RawJob(
            source,
            sourceId,
            title,
            company,
            null,
            postingUrl,
            applyUrl,
            description,
            locationText,
            countryIso,
            null,
            true,
            null,
            compMin,
            compMax,
            compCurrency,
            compPeriod,
            null,
            null,
            ats,
            [],
            postedAt ?? DateTimeOffset.UtcNow.AddDays(-1));
    }
}
