using System.Globalization;
using System.Xml.Linq;
using JobHunter.Sources;

namespace JobHunter.Sources.WeWorkRemotely;

/// <summary>Parses a We Work Remotely category RSS feed into raw jobs.</summary>
public static class WeWorkRemotelyParser
{
    private const string UnknownCompany = "Unknown";

    /// <summary>Parses one RSS response from a We Work Remotely category feed.</summary>
    public static IReadOnlyList<RawJob> Parse(string responseText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(responseText);

        XDocument document = XDocument.Parse(responseText);

        return [.. document.Descendants("item").Select(ParseItem)];
    }

    private static RawJob ParseItem(XElement item)
    {
        string rawTitle = GetElementValue(item, "title") ?? string.Empty;
        (string company, string title) = SplitTitle(rawTitle);

        string link = GetElementValue(item, "link") ?? string.Empty;
        string? guid = GetElementValue(item, "guid");
        string sourceId = string.IsNullOrWhiteSpace(guid) ? link : guid;

        return new RawJob(
            Source: JobSourceKind.WeWorkRemotely,
            SourceId: sourceId,
            Title: title,
            Company: company,
            CompanyUrl: null,
            PostingUrl: link,
            ApplyUrl: link,
            DescriptionRaw: GetElementValue(item, "description") ?? string.Empty,
            LocationText: CombineLocation(GetElementValue(item, "country"), GetElementValue(item, "state")),
            CountryIso: null,
            RegionText: GetElementValue(item, "region"),
            IsRemote: null,
            Language: null,
            CompMin: null,
            CompMax: null,
            CompCurrency: null,
            CompPeriod: null,
            CompSummary: null,
            EmploymentType: GetElementValue(item, "type"),
            Ats: null,
            Tags: ParseSkills(GetElementValue(item, "skills")),
            PostedAt: ParsePostedAt(GetElementValue(item, "pubDate")));
    }

    private static (string Company, string Title) SplitTitle(string rawTitle)
    {
        string trimmed = rawTitle.Trim();
        int separatorIndex = trimmed.IndexOf(": ", StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            return (UnknownCompany, trimmed);
        }

        string company = trimmed[..separatorIndex].Trim();
        string title = trimmed[(separatorIndex + 2)..].Trim();

        return (company.Length > 0 ? company : UnknownCompany, title.Length > 0 ? title : trimmed);
    }

    private static string? CombineLocation(string? country, string? state)
    {
        List<string> parts = [];
        if (!string.IsNullOrWhiteSpace(country))
        {
            parts.Add(country);
        }

        if (!string.IsNullOrWhiteSpace(state))
        {
            parts.Add(state);
        }

        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }

    private static IReadOnlyList<string> ParseSkills(string? skills)
    {
        if (string.IsNullOrWhiteSpace(skills))
        {
            return [];
        }

        return [.. skills.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(StripLeadingAnd)];
    }

    private static string StripLeadingAnd(string skill)
    {
        return skill.StartsWith("and ", StringComparison.OrdinalIgnoreCase) ? skill[4..].Trim() : skill;
    }

    private static DateTimeOffset? ParsePostedAt(string? pubDate)
    {
        return !string.IsNullOrWhiteSpace(pubDate) && DateTimeOffset.TryParse(pubDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsed)
            ? parsed
            : null;
    }

    private static string? GetElementValue(XElement item, string name)
    {
        string? value = item.Element(name)?.Value;

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
