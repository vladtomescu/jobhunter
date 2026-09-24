namespace JobHunter.Sources.Dataset;

/// <summary>The one HTTP client the dataset source uses, and the identifying header every request to the dataset host carries.</summary>
internal static class DatasetHttp
{
    /// <summary>Name of the client configured in the registration file, sized for slice downloads of a few hundred megabytes.</summary>
    internal const string ClientName = "jobhunter-dataset";

    /// <summary>Adds the identifying header to a request without validating it, so an unusual address can never fail a fetch.</summary>
    internal static void AddUserAgent(HttpRequestMessage request, string userAgent)
    {
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
    }
}
