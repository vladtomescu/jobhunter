using System.Buffers;
using System.Security.Cryptography;
using JobHunter.Data;

namespace JobHunter.Sources.Dataset;

/// <summary>A slice ready to be read, and whether this run had to download it.</summary>
public sealed record SliceCacheEntry(string FilePath, bool Downloaded, long SizeBytes);

/// <summary>Keeps one downloaded parquet slice per applicant tracking system in the data folder, downloading again only when the manifest checksum differs from the cached one.</summary>
public sealed class SliceDownloader(IHttpClientFactory httpClientFactory, DataPaths dataPaths)
{
    private const string SliceFileName = "jobs.parquet";

    private const string ChecksumSuffix = ".sha256";

    private const string PartialSuffix = ".download";

    private const int CopyBufferSize = 1 << 20;

    /// <summary>Returns the cached slice when its recorded checksum still matches the manifest, and otherwise downloads, verifies and replaces it.</summary>
    public async Task<SliceCacheEntry> EnsureAsync(DatasetSlice slice, string userAgent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(slice);

        if (string.IsNullOrWhiteSpace(slice.ParquetSha256))
        {
            throw new InvalidOperationException($"The manifest carries no checksum for the {slice.Ats} slice, so the download cannot be verified.");
        }

        string folder = Path.Combine(dataPaths.Dataset, slice.Ats);
        Directory.CreateDirectory(folder);

        string filePath = Path.Combine(folder, SliceFileName);
        string checksumPath = filePath + ChecksumSuffix;

        if (await IsCachedAsync(filePath, checksumPath, slice.ParquetSha256, cancellationToken))
        {
            return new SliceCacheEntry(filePath, Downloaded: false, new FileInfo(filePath).Length);
        }

        string partialPath = filePath + PartialSuffix;
        string downloadedChecksum = await DownloadAsync(slice.ParquetUrl, partialPath, userAgent, cancellationToken);

        if (!string.Equals(downloadedChecksum, slice.ParquetSha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(partialPath);

            throw new InvalidOperationException($"The downloaded {slice.Ats} slice hashes to {downloadedChecksum}, but the manifest expects {slice.ParquetSha256}.");
        }

        File.Move(partialPath, filePath, overwrite: true);
        await File.WriteAllTextAsync(checksumPath, downloadedChecksum, cancellationToken);

        return new SliceCacheEntry(filePath, Downloaded: true, new FileInfo(filePath).Length);
    }

    private static async Task<bool> IsCachedAsync(string filePath, string checksumPath, string expectedChecksum, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath) || !File.Exists(checksumPath))
        {
            return false;
        }

        string cachedChecksum = (await File.ReadAllTextAsync(checksumPath, cancellationToken)).Trim();

        return string.Equals(cachedChecksum, expectedChecksum, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> DownloadAsync(string url, string partialPath, string userAgent, CancellationToken cancellationToken)
    {
        HttpClient client = httpClientFactory.CreateClient(DatasetHttp.ClientName);

        using HttpRequestMessage request = new(HttpMethod.Get, url);
        DatasetHttp.AddUserAgent(request, userAgent);

        using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using FileStream target = new(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize, useAsync: true);

        using IncrementalHash checksum = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);

        try
        {
            int read;

            while ((read = await source.ReadAsync(buffer.AsMemory(0, CopyBufferSize), cancellationToken)) > 0)
            {
                checksum.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return Convert.ToHexStringLower(checksum.GetHashAndReset());
    }
}
