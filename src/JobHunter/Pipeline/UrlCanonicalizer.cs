namespace JobHunter.Pipeline;

/// <summary>Reduces an apply or posting URL to the form two sightings of the same job share: lower-case host, no tracking parameters, no fragment.</summary>
public static class UrlCanonicalizer
{
    private static readonly string[] TrackingParameters = ["ref", "gh_src", "lever-source", "source"];

    /// <summary>Canonicalizes a URL; a value that is not an absolute URL comes back trimmed and otherwise untouched.</summary>
    public static string Canonicalize(string url)
    {
        ArgumentNullException.ThrowIfNull(url);

        string trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? parsed) || !parsed.IsAbsoluteUri)
        {
            return trimmed;
        }

        string scheme = parsed.Scheme.ToLowerInvariant();
        string host = parsed.Host.ToLowerInvariant();
        string port = parsed.IsDefaultPort ? string.Empty : $":{parsed.Port}";
        string path = TrimTrailingSlash(parsed.AbsolutePath);
        string query = KeepMeaningfulParameters(parsed.Query);

        return $"{scheme}://{host}{port}{path}{query}";
    }

    /// <summary>True when the parameter only says where the click came from, which never changes which job is being described.</summary>
    public static bool IsTrackingParameter(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return name.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)
            || TrackingParameters.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    private static string TrimTrailingSlash(string path)
    {
        return path.Length > 1 && path.EndsWith('/') ? path.TrimEnd('/') : path;
    }

    private static string KeepMeaningfulParameters(string query)
    {
        List<string> kept = [];
        foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=');
            string name = separator < 0 ? pair : pair[..separator];
            if (!IsTrackingParameter(name))
            {
                kept.Add(pair);
            }
        }

        return kept.Count == 0 ? string.Empty : $"?{string.Join('&', kept)}";
    }
}
