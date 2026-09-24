namespace JobHunter.Jobs;

/// <summary>One suggested Google search for finding a paywalled board posting on the employer's own careers page or ATS, where applying is free.</summary>
public sealed record BoardSearchLink(string Label, string Url);

/// <summary>Recognizes a WeWorkRemotely or RemoteOK posting page, whose Apply flow sits behind a paid subscription, and builds the Google searches that help find the same job where applying is free.</summary>
public static class PaywalledBoardSearch
{
    /// <summary>True when the link is a WeWorkRemotely or RemoteOK page.</summary>
    public static bool IsPaywalledBoard(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? parsed))
        {
            return false;
        }

        string host = parsed.Host.ToLowerInvariant();

        return Matches(host, "weworkremotely.com") || Matches(host, "remoteok.com") || Matches(host, "remoteok.io");
    }

    /// <summary>The three searches offered below a paywalled board link: company and title, the company's careers page, and the same restricted to the ATS forms prefill can already fill.</summary>
    public static IReadOnlyList<BoardSearchLink> BuildSearchLinks(string company, string title)
    {
        string safeCompany = StripQuotes(company);
        string safeTitle = StripQuotes(title);

        string companyAndTitleQuery = $"\"{safeCompany}\" \"{safeTitle}\"";
        string careersQuery = $"\"{safeCompany}\" careers";
        string atsFormsQuery = $"\"{safeCompany}\" \"{safeTitle}\" (site:greenhouse.io OR site:lever.co OR site:ashbyhq.com)";

        return
        [
            new BoardSearchLink(companyAndTitleQuery, SearchUrl(companyAndTitleQuery)),
            new BoardSearchLink(careersQuery, SearchUrl(careersQuery)),
            new BoardSearchLink("Greenhouse / Lever / Ashby form (prefill-ready)", SearchUrl(atsFormsQuery))
        ];
    }

    private static string SearchUrl(string query)
    {
        return $"https://www.google.com/search?q={Uri.EscapeDataString(query)}";
    }

    private static string StripQuotes(string value)
    {
        return value.Replace("\"", string.Empty, StringComparison.Ordinal).Trim();
    }

    private static bool Matches(string host, string domain)
    {
        return string.Equals(host, domain, StringComparison.Ordinal) || host.EndsWith($".{domain}", StringComparison.Ordinal);
    }
}
