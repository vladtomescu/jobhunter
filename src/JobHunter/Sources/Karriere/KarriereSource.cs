using System.Net;
using JobHunter.Sources;

namespace JobHunter.Sources.Karriere;

/// <summary>How long the karriere.at source waits between two requests, so a refresh never hammers the site.</summary>
public sealed record KarrierePacing(TimeSpan PauseBetweenRequests);

/// <summary>Runs the karriere.at searches from the settings, page by page, then reads every job page they list once; result pages are cached raw, job pages are not, and a failed search or job page is reported through Error, never thrown.</summary>
public sealed class KarriereSource(HttpClient httpClient, KarrierePacing pacing) : IJobSource
{
    /// <summary>The most result pages one search reads, about three hundred jobs.</summary>
    public const int MaxPagesPerSearch = 20;

    private bool hasSentRequest;

    public JobSourceKind Kind => JobSourceKind.Karriere;

    public bool IsFullSnapshot => false;

    /// <summary>Collects the listings of every search, then reads each listed job page, keeping the jobs that could be read even when a search or a page fails.</summary>
    public async Task<SourceFetchResult> FetchAsync(SourceFetchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        List<string> errors = [];
        Dictionary<string, KarriereListing> listings = [];
        IReadOnlyList<KarriereSearch> searches = KarriereSearch.ReadAll(context.Settings.KarriereSearches);

        for (int searchIndex = 0; searchIndex < searches.Count; searchIndex++)
        {
            KarriereSearch search = searches[searchIndex];
            try
            {
                await CollectListingsAsync(search, searchIndex + 1, context, listings, errors, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                errors.Add($"{search}: {exception.Message}");
            }
        }

        List<RawJob> jobs = [];
        int unreadPages = 0;
        string? lastPageError = null;
        foreach (KarriereListing listing in listings.Values)
        {
            try
            {
                string jobPage = await GetAsync(new Uri(listing.Link), asJson: false, context.Settings.Email, cancellationToken) ?? string.Empty;
                jobs.Add(KarriereParser.ParseJob(listing, jobPage));
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                unreadPages++;
                lastPageError = $"{listing.Link}: {exception.Message}";
            }
        }

        if (unreadPages > 0)
        {
            errors.Add($"{unreadPages} job page(s) could not be read, the last one {lastPageError}");
        }

        string? error = errors.Count > 0 ? string.Join(" | ", errors) : null;

        return new SourceFetchResult(jobs, listings.Count, error);
    }

    private async Task CollectListingsAsync(KarriereSearch search, int searchNumber, SourceFetchContext context, Dictionary<string, KarriereListing> listings, List<string> errors, CancellationToken cancellationToken)
    {
        for (int page = 1; page <= MaxPagesPerSearch; page++)
        {
            string? responseText = await GetAsync(search.PageUrl(page), asJson: true, context.Settings.Email, cancellationToken);
            if (responseText is null)
            {
                return;
            }

            KarriereSearchPage result = KarriereParser.ParseSearchPage(responseText);
            foreach (KarriereListing listing in result.Listings)
            {
                listings.TryAdd(listing.Id, listing);
            }

            if (await CacheRawResponseAsync(context.RawCacheFolder, searchNumber, page, responseText, cancellationToken) is string cacheError)
            {
                errors.Add($"{search}: {cacheError}");
            }

            if (!result.HasMore || result.Listings.Count == 0)
            {
                return;
            }
        }
    }

    /// <summary>Sends one request after the pause, asking for JSON the way the site's own result page does; a result page past the last one answers 410 Gone, which reads as no more results.</summary>
    private async Task<string?> GetAsync(Uri url, bool asJson, string email, CancellationToken cancellationToken)
    {
        if (hasSentRequest && pacing.PauseBetweenRequests > TimeSpan.Zero)
        {
            await Task.Delay(pacing.PauseBetweenRequests, cancellationToken);
        }

        hasSentRequest = true;

        using HttpRequestMessage request = new(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(SourceUserAgent.Build(email));
        if (asJson)
        {
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        }

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        if (asJson && response.StatusCode == HttpStatusCode.Gone)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    /// <summary>Writes one result page to the cache folder, creating it when it is not there yet; a cache that cannot be written is reported, never allowed to void the postings already fetched.</summary>
    private static async Task<string?> CacheRawResponseAsync(string rawCacheFolder, int searchNumber, int page, string responseText, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(rawCacheFolder);

            string fileName = $"karriere-search{searchNumber}-page{page}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.json";

            await File.WriteAllTextAsync(Path.Combine(rawCacheFolder, fileName), responseText, cancellationToken);

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return $"the raw response could not be cached: {exception.Message}";
        }
    }
}
