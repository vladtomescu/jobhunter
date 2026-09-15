using System.Text.Json;
using JobHunter.Data;

namespace JobHunter.Sources.Dataset;

/// <summary>Reads the dataset manifest, which names one parquet slice per applicant tracking system together with its checksum, size and row count.</summary>
public sealed class ManifestClient(IHttpClientFactory httpClientFactory, DataPaths dataPaths)
{
    /// <summary>Where the manifest lives.</summary>
    public const string ManifestUrl = "https://storage.stapply.ai/jobhive/v1/manifest.json";

    private const string CachedManifestFileName = "manifest.json";

    /// <summary>Fetches the manifest, keeps a copy next to the downloaded slices and returns the slices it describes.</summary>
    public async Task<DatasetManifest> GetAsync(string userAgent, CancellationToken cancellationToken)
    {
        HttpClient client = httpClientFactory.CreateClient(DatasetHttp.ClientName);

        using HttpRequestMessage request = new(HttpMethod.Get, ManifestUrl);
        DatasetHttp.AddUserAgent(request, userAgent);

        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(dataPaths.Dataset, CachedManifestFileName), json, cancellationToken);

        return Parse(json);
    }

    /// <summary>Turns the manifest document into the slices the source can download.</summary>
    public static DatasetManifest Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        Dictionary<string, DatasetSlice> slices = new(StringComparer.OrdinalIgnoreCase);

        if (root.TryGetProperty("by_ats", out JsonElement byAts) && byAts.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty entry in byAts.EnumerateObject())
            {
                string? parquetUrl = ReadString(entry.Value, "parquet");

                if (string.IsNullOrWhiteSpace(parquetUrl))
                {
                    continue;
                }

                slices[entry.Name] = new DatasetSlice(
                    entry.Name,
                    parquetUrl,
                    ReadString(entry.Value, "parquet_sha256") ?? string.Empty,
                    ReadNumber(entry.Value, "parquet_size_bytes"),
                    ReadNumber(entry.Value, "rows"));
            }
        }

        return new DatasetManifest(ReadString(root, "version") ?? string.Empty, ReadTimestamp(root, "generated_at"), slices);
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static long ReadNumber(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number)
            ? number
            : 0;
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement value) && value.TryGetDateTimeOffset(out DateTimeOffset timestamp)
            ? timestamp
            : null;
    }
}
