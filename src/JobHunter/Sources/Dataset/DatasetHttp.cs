namespace JobHunter.Sources.Dataset;

/// <summary>The one HTTP client the dataset source uses, and the identifying header every request to the dataset host carries.</summary>
internal static class DatasetHttp
{
    /// <summary>Name of the client configured in the registration file, sized for slice downloads of a few hundred megabytes.</summary>
    internal const string ClientName = "jobhunter-dataset";

    /// <summary>Builds the identifying header from the address in the settings, falling back to a neutral one while the address is still empty.</summary>
    internal static string BuildUserAgent(string email)
    {
        return string.IsNullOrWhiteSpace(email)
            ? "JobHunter/1.0 (personal)"
            : $"JobHunter/1.0 (personal; +mailto:{email.Trim()})";
    }

    /// <summary>Adds the identifying header to a request without validating it, so an unusual address can never fail a fetch.</summary>
    internal static void AddUserAgent(HttpRequestMessage request, string userAgent)
    {
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
    }
}
