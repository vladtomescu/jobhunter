using JobHunter.Domain;

namespace JobHunter.Pipeline;

/// <summary>Recognizes the applicant tracking system behind a posting, from the name the dataset reports or from the host of the apply URL.</summary>
public static class AtsKindParser
{
    /// <summary>Reads the system from the dataset's name for it; a name that is known but not one of the supported systems counts as another system.</summary>
    public static AtsKind? FromName(string? atsType)
    {
        if (string.IsNullOrWhiteSpace(atsType))
        {
            return null;
        }

        return atsType.Trim().ToLowerInvariant() switch
        {
            "greenhouse" => AtsKind.Greenhouse,
            "lever" => AtsKind.Lever,
            "ashby" or "ashbyhq" => AtsKind.Ashby,
            "workable" => AtsKind.Workable,
            "smartrecruiters" => AtsKind.SmartRecruiters,
            _ => AtsKind.Other
        };
    }

    /// <summary>Reads the system from the host of an apply URL; a host that belongs to no known system stays unknown.</summary>
    public static AtsKind? FromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? parsed))
        {
            return null;
        }

        string host = parsed.Host.ToLowerInvariant();

        return host switch
        {
            _ when Matches(host, "greenhouse.io") => AtsKind.Greenhouse,
            _ when Matches(host, "lever.co") => AtsKind.Lever,
            _ when Matches(host, "ashbyhq.com") => AtsKind.Ashby,
            _ when Matches(host, "workable.com") => AtsKind.Workable,
            _ when Matches(host, "smartrecruiters.com") => AtsKind.SmartRecruiters,
            _ => null
        };
    }

    /// <summary>Reads the system from the reported name first and falls back to the apply URL.</summary>
    public static AtsKind? Parse(string? atsType, string? url)
    {
        return FromName(atsType) ?? FromUrl(url);
    }

    private static bool Matches(string host, string domain)
    {
        return string.Equals(host, domain, StringComparison.Ordinal) || host.EndsWith($".{domain}", StringComparison.Ordinal);
    }
}
