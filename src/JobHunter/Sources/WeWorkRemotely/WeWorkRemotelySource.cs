using JobHunter.Sources;

namespace JobHunter.Sources.WeWorkRemotely;

/// <summary>Fetches the three We Work Remotely category feeds, caches each raw response and parses it into raw jobs; a failed feed is reported through Error, never thrown.</summary>
public sealed class WeWorkRemotelySource(HttpClient httpClient) : IJobSource
{
    public JobSourceKind Kind => JobSourceKind.WeWorkRemotely;

    public bool IsFullSnapshot => false;

    /// <summary>Fetches, caches and parses every category feed, keeping the jobs from feeds that succeed even when another feed fails.</summary>
    public async Task<SourceFetchResult> FetchAsync(SourceFetchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        List<RawJob> jobs = [];
        List<string> errors = [];

        foreach (string feedUrl in WeWorkRemotelyFeeds.CategoryUrls)
        {
            try
            {
                string responseText = await FetchResponseTextAsync(feedUrl, context.Settings.Email, cancellationToken);

                jobs.AddRange(WeWorkRemotelyParser.Parse(responseText));

                if (await CacheRawResponseAsync(context.RawCacheFolder, feedUrl, responseText, cancellationToken) is string cacheError)
                {
                    errors.Add($"{FeedName(feedUrl)}: {cacheError}");
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                errors.Add($"{FeedName(feedUrl)}: {exception.Message}");
            }
        }

        string? error = errors.Count > 0 ? string.Join(" | ", errors) : null;

        return new SourceFetchResult(jobs, jobs.Count, error);
    }

    private async Task<string> FetchResponseTextAsync(string feedUrl, string email, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, feedUrl);
        request.Headers.UserAgent.ParseAdd(SourceUserAgent.Build(email));

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    /// <summary>Writes one feed response to the cache folder, creating it when it is not there yet; a cache that cannot be written is reported, never allowed to void the postings already fetched.</summary>
    private static async Task<string?> CacheRawResponseAsync(string rawCacheFolder, string feedUrl, string responseText, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(rawCacheFolder);

            string fileName = $"wwr-{FeedName(feedUrl)}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.rss";
            string path = Path.Combine(rawCacheFolder, fileName);

            await File.WriteAllTextAsync(path, responseText, cancellationToken);

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return $"the raw response could not be cached: {exception.Message}";
        }
    }

    private static string FeedName(string feedUrl)
    {
        return Path.GetFileNameWithoutExtension(new Uri(feedUrl).Segments[^1]);
    }
}
