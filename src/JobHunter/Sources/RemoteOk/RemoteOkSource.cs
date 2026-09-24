using JobHunter.Sources;

namespace JobHunter.Sources.RemoteOk;

/// <summary>Fetches the current RemoteOK postings, caches the raw response and parses it into raw jobs; a failed request is reported through Error, never thrown.</summary>
public sealed class RemoteOkSource(HttpClient httpClient) : IJobSource
{
    private const string RequestUri = "https://remoteok.com/api";

    public JobSourceKind Kind => JobSourceKind.RemoteOk;

    public bool IsFullSnapshot => false;

    /// <summary>Fetches, caches and parses the current RemoteOK postings.</summary>
    public async Task<SourceFetchResult> FetchAsync(SourceFetchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            string responseText = await FetchResponseTextAsync(context.Settings.Email, cancellationToken);

            IReadOnlyList<RawJob> jobs = RemoteOkParser.Parse(responseText);
            string? cacheError = await CacheRawResponseAsync(context.RawCacheFolder, responseText, cancellationToken);

            return new SourceFetchResult(jobs, jobs.Count, cacheError);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new SourceFetchResult([], 0, exception.Message);
        }
    }

    private async Task<string> FetchResponseTextAsync(string email, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, RequestUri);
        request.Headers.UserAgent.ParseAdd(SourceUserAgent.Build(email));

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    /// <summary>Writes the response to the cache folder, creating it when it is not there yet; a cache that cannot be written is reported, never allowed to void the postings already fetched.</summary>
    private static async Task<string?> CacheRawResponseAsync(string rawCacheFolder, string responseText, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(rawCacheFolder);

            string fileName = $"remoteok-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.json";
            string path = Path.Combine(rawCacheFolder, fileName);

            await File.WriteAllTextAsync(path, responseText, cancellationToken);

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return $"the raw response could not be cached: {exception.Message}";
        }
    }
}
