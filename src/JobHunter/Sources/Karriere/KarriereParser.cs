using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using JobHunter.Domain;
using JobHunter.Sources;

namespace JobHunter.Sources.Karriere;

/// <summary>One job as a karriere.at result page lists it; the job page fills in the rest.</summary>
public sealed record KarriereListing(string Id, string Link, string Title, string? Company, IReadOnlyList<string> Locations, string? Salary, string? Summary, string? EmploymentTypes);

/// <summary>One karriere.at result page: its jobs and whether a further page follows.</summary>
public sealed record KarriereSearchPage(IReadOnlyList<KarriereListing> Listings, bool HasMore);

/// <summary>Parses karriere.at result pages, which the site serves as JSON, and job pages, whose schema.org JobPosting block carries the description, places, pay and posting date.</summary>
public static partial class KarriereParser
{
    private const string UnknownCompany = "Unknown";

    /// <summary>Austrian salaries are paid fourteen times a year, so a monthly figure is annualized at fourteen months.</summary>
    private const int AustrianSalaryPaymentsPerYear = 14;

    /// <summary>The word a listing's salary starts with when it quotes only the floor Austrian law makes a posting state, "ab 3.400 € monatlich".</summary>
    private const string FloorPrefix = "ab ";

    /// <summary>Parses one result page; entries that are not jobs, such as advertising slots, are skipped.</summary>
    public static KarriereSearchPage ParseSearchPage(string responseText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(responseText);

        using JsonDocument document = JsonDocument.Parse(responseText);
        JsonElement list = document.RootElement.GetProperty("data").GetProperty("jobsSearchList");

        List<KarriereListing> listings = [];
        if (list.TryGetProperty("activeItems", out JsonElement activeItems) && activeItems.TryGetProperty("items", out JsonElement items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in items.EnumerateArray())
            {
                if (item.TryGetProperty("jobsItem", out JsonElement job) && ParseListing(job) is KarriereListing listing)
                {
                    listings.Add(listing);
                }
            }
        }

        bool hasMore = list.TryGetProperty("loadMoreJobsButton", out JsonElement loadMore)
            && loadMore.TryGetProperty("active", out JsonElement active)
            && active.ValueKind == JsonValueKind.True;

        return new KarriereSearchPage(listings, hasMore);
    }

    /// <summary>Builds the raw job from a listing and its job page; a page without a JobPosting block leaves the listing's own fields, with its summary as the description.</summary>
    public static RawJob ParseJob(KarriereListing listing, string jobPageHtml)
    {
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentNullException.ThrowIfNull(jobPageHtml);

        using JsonDocument? posting = FindJobPosting(jobPageHtml);
        JsonElement? element = posting is null ? null : FindJobPostingElement(posting.RootElement);

        if (element is not JsonElement job)
        {
            return Build(listing, listing.Title, listing.Company, null, listing.Summary ?? string.Empty, JoinPlaces(listing.Locations), null, null, listing.EmploymentTypes, null);
        }

        JsonElement? organization = job.TryGetProperty("hiringOrganization", out JsonElement hiringOrganization) ? hiringOrganization : null;
        (IReadOnlyList<string> localities, string? countryIso) = ReadPlaces(job);

        return Build(
            listing,
            GetString(job, "title") ?? listing.Title,
            (organization is JsonElement named ? GetString(named, "name") : null) ?? listing.Company,
            organization is JsonElement linked ? GetString(linked, "sameAs") : null,
            GetString(job, "description") ?? listing.Summary ?? string.Empty,
            JoinPlaces(localities.Count > 0 ? localities : listing.Locations),
            countryIso,
            ReadSalary(job, listing),
            ReadEmploymentType(job) ?? listing.EmploymentTypes,
            ReadPostedAt(job));
    }

    private static RawJob Build(KarriereListing listing, string title, string? company, string? companyUrl, string description, string? location, string? countryIso, Salary? salary, string? employmentType, DateTimeOffset? postedAt)
    {
        return new RawJob(
            Source: JobSourceKind.Karriere,
            SourceId: listing.Id,
            Title: title,
            Company: string.IsNullOrWhiteSpace(company) ? UnknownCompany : company,
            CompanyUrl: companyUrl,
            PostingUrl: listing.Link,
            ApplyUrl: listing.Link,
            DescriptionRaw: description,
            LocationText: location,
            CountryIso: countryIso,
            RegionText: null,
            IsRemote: null,
            Language: null,
            CompMin: salary?.Min,
            CompMax: salary?.Max,
            CompCurrency: salary?.Currency,
            CompPeriod: salary?.Period,
            CompSummary: listing.Salary,
            EmploymentType: employmentType,
            Ats: null,
            Tags: [],
            PostedAt: postedAt);
    }

    private static KarriereListing? ParseListing(JsonElement job)
    {
        string? id = GetString(job, "id");
        string? link = GetString(job, "link");
        string? title = GetString(job, "title");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(link) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        string? company = job.TryGetProperty("company", out JsonElement companyElement) && companyElement.ValueKind == JsonValueKind.Object ? GetString(companyElement, "name") : null;
        IReadOnlyList<string> locations = job.TryGetProperty("locations", out JsonElement locationsElement) && locationsElement.ValueKind == JsonValueKind.Array
            ? [.. locationsElement.EnumerateArray().Where(location => location.ValueKind == JsonValueKind.Object).Select(location => GetString(location, "name")).OfType<string>()]
            : [];

        return new KarriereListing(id, link, title, company, locations, GetString(job, "salary"), GetString(job, "summary"), GetString(job, "employmentTypes"));
    }

    private static JsonDocument? FindJobPosting(string html)
    {
        foreach (Match match in LinkedDataBlock().Matches(html))
        {
            try
            {
                JsonDocument document = JsonDocument.Parse(match.Groups["json"].Value);
                if (FindJobPostingElement(document.RootElement) is not null)
                {
                    return document;
                }

                document.Dispose();
            }
            catch (JsonException)
            {
                // A block that is not valid JSON carries no posting; the next one may.
            }
        }

        return null;
    }

    private static JsonElement? FindJobPostingElement(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray().Select(FindJobPostingElement).FirstOrDefault(element => element is not null);
        }

        return root.ValueKind == JsonValueKind.Object && GetString(root, "@type") == "JobPosting" ? root : null;
    }

    private static (IReadOnlyList<string> Localities, string? CountryIso) ReadPlaces(JsonElement job)
    {
        if (!job.TryGetProperty("jobLocation", out JsonElement locations))
        {
            return ([], null);
        }

        IEnumerable<JsonElement> places = locations.ValueKind == JsonValueKind.Array ? locations.EnumerateArray() : [locations];
        List<string> localities = [];
        HashSet<string> countries = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement place in places)
        {
            if (place.ValueKind != JsonValueKind.Object || !place.TryGetProperty("address", out JsonElement address) || address.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (GetString(address, "addressLocality") is string locality)
            {
                localities.Add(locality);
            }

            if (GetString(address, "addressCountry") is string country)
            {
                countries.Add(country);
            }
        }

        return (localities, countries.Count == 1 ? countries.Single().ToUpperInvariant() : null);
    }

    /// <summary>Reads the pay the posting offers; a lone floor ("ab" with no upper end) is the legal minimum, not the pay for the role, so it records no pay and stays visible only as the listing's salary text.</summary>
    private static Salary? ReadSalary(JsonElement job, KarriereListing listing)
    {
        if (!job.TryGetProperty("baseSalary", out JsonElement baseSalary) || baseSalary.ValueKind != JsonValueKind.Object
            || !baseSalary.TryGetProperty("value", out JsonElement value) || value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        decimal? min = GetPositiveDecimal(value, "minValue") ?? GetPositiveDecimal(value, "value");
        decimal? max = GetPositiveDecimal(value, "maxValue");
        bool floorOnly = max is null && listing.Salary is string salary && salary.StartsWith(FloorPrefix, StringComparison.OrdinalIgnoreCase);
        if ((min is null && max is null) || floorOnly)
        {
            return null;
        }

        string? unit = GetString(value, "unitText")?.ToUpperInvariant();
        (CompPeriod? period, int multiplier) = unit switch
        {
            "YEAR" => (CompPeriod.Year, 1),
            "MONTH" => (CompPeriod.Year, AustrianSalaryPaymentsPerYear),
            "DAY" => (CompPeriod.Day, 1),
            "HOUR" => (CompPeriod.Hour, 1),
            _ => ((CompPeriod?)null, 1)
        };

        return new Salary(min * multiplier, max * multiplier, GetString(baseSalary, "currency"), period);
    }

    private static string? ReadEmploymentType(JsonElement job)
    {
        if (!job.TryGetProperty("employmentType", out JsonElement employmentType))
        {
            return null;
        }

        return employmentType.ValueKind switch
        {
            JsonValueKind.String => employmentType.GetString(),
            JsonValueKind.Array => string.Join(", ", employmentType.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString())),
            _ => null
        };
    }

    private static DateTimeOffset? ReadPostedAt(JsonElement job)
    {
        return GetString(job, "datePosted") is string datePosted && DateTimeOffset.TryParse(datePosted, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsed)
            ? parsed
            : null;
    }

    private static string? JoinPlaces(IReadOnlyList<string> places)
    {
        return places.Count > 0 ? string.Join("; ", places.Distinct(StringComparer.OrdinalIgnoreCase)) : null;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;
    }

    private static decimal? GetPositiveDecimal(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out decimal number) && number > 0
            ? number
            : null;
    }

    private sealed record Salary(decimal? Min, decimal? Max, string? Currency, CompPeriod? Period);

    [GeneratedRegex(@"<script[^>]*type=""application/ld\+json""[^>]*>(?<json>.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex LinkedDataBlock();
}
