using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using JobHunter.Domain;
using JobHunter.Sources;

namespace JobHunter.Sources.RemoteOk;

/// <summary>Parses the JSON array RemoteOK returns from https://remoteok.com/api; element 0 is a legal notice and is skipped, and every description loses the anti-spam note the board appends to it.</summary>
public static partial class RemoteOkParser
{
    private const string CompCurrency = "USD";

    /// <summary>Parses a RemoteOK API response into raw jobs.</summary>
    public static IReadOnlyList<RawJob> Parse(string responseText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(responseText);

        using JsonDocument document = JsonDocument.Parse(responseText);
        List<RawJob> jobs = [];

        int index = 0;
        foreach (JsonElement element in document.RootElement.EnumerateArray())
        {
            if (index++ == 0)
            {
                continue;
            }

            RawJob? job = ParseJob(element);
            if (job is not null)
            {
                jobs.Add(job);
            }
        }

        return jobs;
    }

    private static RawJob? ParseJob(JsonElement element)
    {
        string? id = GetString(element, "id");
        string? slug = GetString(element, "slug");
        string sourceId = !string.IsNullOrWhiteSpace(id) ? id : slug ?? string.Empty;
        string? position = GetString(element, "position");
        string? company = GetString(element, "company");
        string? url = GetString(element, "url");

        if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(position) || string.IsNullOrWhiteSpace(company) || string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        decimal? compMin = GetPositiveDecimal(element, "salary_min");
        decimal? compMax = GetPositiveDecimal(element, "salary_max");
        bool hasComp = compMin.HasValue || compMax.HasValue;
        string? location = GetString(element, "location");

        return new RawJob(
            Source: JobSourceKind.RemoteOk,
            SourceId: sourceId,
            Title: position.Trim(),
            Company: company.Trim(),
            CompanyUrl: null,
            PostingUrl: url,
            ApplyUrl: GetString(element, "apply_url") ?? url,
            DescriptionRaw: WithoutAntiSpamNote(GetString(element, "description") ?? string.Empty),
            LocationText: string.IsNullOrWhiteSpace(location) ? null : location.Trim(),
            CountryIso: null,
            RegionText: null,
            IsRemote: null,
            Language: null,
            CompMin: compMin,
            CompMax: compMax,
            CompCurrency: hasComp ? CompCurrency : null,
            CompPeriod: hasComp ? CompPeriod.Year : null,
            CompSummary: null,
            EmploymentType: null,
            Ats: null,
            Tags: GetStringArray(element, "tags"),
            PostedAt: GetPostedAt(element));
    }

    /// <summary>Removes the note that asks applicants to quote a word and a tag, together with the line breaks before it; the word varies and the tag follows the address that fetched the feed, so the note would make an unchanged posting read as rewritten.</summary>
    private static string WithoutAntiSpamNote(string description)
    {
        return AntiSpamNote().Replace(description, string.Empty);
    }

    private static DateTimeOffset? GetPostedAt(JsonElement element)
    {
        if (element.TryGetProperty("date", out JsonElement dateElement) && dateElement.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(dateElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsedDate))
        {
            return parsedDate;
        }

        if (element.TryGetProperty("epoch", out JsonElement epochElement) && epochElement.ValueKind == JsonValueKind.Number
            && epochElement.TryGetInt64(out long epochSeconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(epochSeconds);
        }

        return null;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static decimal? GetPositiveDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
        {
            return null;
        }

        decimal? amount = value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out decimal number) => number,
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed) => parsed,
            _ => null
        };

        return amount is > 0 ? amount : null;
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)];
    }

    [GeneratedRegex(@"(?:<br\s*/?>|\s)*Please mention the word\s+\S+\s+(?:and tag\s+\S+\s+)?when applying to show you read the job post completely(?:\s*\(#[^)]*\))?\.(?:\s*This is a beta feature[^.]*\.)?(?:\s*Companies can search these words[^.]*\.)?", RegexOptions.IgnoreCase)]
    private static partial Regex AntiSpamNote();
}
