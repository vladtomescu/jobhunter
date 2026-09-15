namespace JobHunter.Sources.Dataset;

/// <summary>One applicant tracking system slice of the dataset, as the manifest describes it.</summary>
public sealed record DatasetSlice(string Ats, string ParquetUrl, string ParquetSha256, long ParquetSizeBytes, long Rows);

/// <summary>The dataset manifest: the schema version, when the dataset was generated and one parquet slice per applicant tracking system.</summary>
public sealed record DatasetManifest(string Version, DateTimeOffset? GeneratedAt, IReadOnlyDictionary<string, DatasetSlice> Slices);
