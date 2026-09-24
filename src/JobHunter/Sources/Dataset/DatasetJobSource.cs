namespace JobHunter.Sources.Dataset;

/// <summary>Reads the applicant tracking system dataset: one parquet slice per system named in the settings, downloaded only when its checksum changed.</summary>
public sealed class DatasetJobSource(ManifestClient manifestClient, SliceDownloader sliceDownloader, DatasetJobReader reader, ILogger<DatasetJobSource> logger) : IJobSource
{
    /// <summary>Which source this is.</summary>
    public JobSourceKind Kind => JobSourceKind.Dataset;

    /// <summary>Every slice lists all the postings the system currently offers, which is what lets the liveness pass mark absent jobs inactive.</summary>
    public bool IsFullSnapshot => true;

    /// <summary>Reads every slice named in the settings; a slice that fails is reported while the others still contribute their postings.</summary>
    public async Task<SourceFetchResult> FetchAsync(SourceFetchContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        string userAgent = SourceUserAgent.Build(context.Settings.Email);
        DatasetManifest manifest;

        try
        {
            manifest = await manifestClient.GetAsync(userAgent, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new SourceFetchResult([], 0, $"manifest: {exception.Message}");
        }

        List<RawJob> jobs = [];
        List<string> errors = [];
        DatasetReadFilter filter = new(context.NotBefore, context.Candidate);
        int fetched = 0;

        foreach (string ats in ParseAtsList(context.Settings.DatasetAtsList))
        {
            if (!manifest.Slices.TryGetValue(ats, out DatasetSlice? slice))
            {
                errors.Add($"{ats}: the manifest lists no slice for this applicant tracking system");

                continue;
            }

            try
            {
                SliceCacheEntry cached = await sliceDownloader.EnsureAsync(slice, userAgent, cancellationToken);

                logger.LogInformation("Dataset slice {Ats}: {Rows} rows, {Bytes} bytes, {State}.", slice.Ats, slice.Rows, cached.SizeBytes, cached.Downloaded ? "downloaded" : "unchanged since the last run");

                await foreach (RawJob job in reader.ReadAsync(cached.FilePath, filter, cancellationToken))
                {
                    jobs.Add(job);
                }

                fetched += (int)slice.Rows;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                errors.Add($"{ats}: {exception.Message}");
            }
        }

        return new SourceFetchResult(jobs, fetched, errors.Count == 0 ? null : string.Join("; ", errors));
    }

    private static IEnumerable<string> ParseAtsList(string datasetAtsList)
    {
        return datasetAtsList
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
