using System.Globalization;
using System.Runtime.CompilerServices;
using JobHunter.Domain;
using JobHunter.Pipeline;
using Parquet;
using Parquet.Schema;

namespace JobHunter.Sources.Dataset;

/// <summary>What the reader keeps out of a slice: postings no older than the intake window whose title passes the deterministic rules.</summary>
public sealed record DatasetReadFilter(DateTimeOffset NotBefore, ITitleRules TitleRules);

/// <summary>Streams postings out of one parquet slice one row group at a time, decoding only the columns the requirements name and only for the rows that survive the filter.</summary>
public sealed class DatasetJobReader
{
    private const int FileBufferSize = 1 << 16;

    /// <summary>Yields one posting per surviving row; the slice as a whole is never held in memory, only the columns of the row group being examined.</summary>
    public async IAsyncEnumerable<RawJob> ReadAsync(string parquetFilePath, DatasetReadFilter filter, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parquetFilePath);
        ArgumentNullException.ThrowIfNull(filter);

        await using FileStream stream = new(parquetFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, FileBufferSize, useAsync: true);
        await using ParquetReader reader = await ParquetReader.CreateAsync(stream, cancellationToken: cancellationToken);

        Dictionary<string, DataField> fields = IndexFields(reader.Schema);

        if (!fields.ContainsKey(DatasetColumns.Url) || !fields.ContainsKey(DatasetColumns.Title))
        {
            throw new InvalidOperationException($"The slice at {parquetFilePath} carries neither a {DatasetColumns.Url} nor a {DatasetColumns.Title} column, so no posting can be identified.");
        }

        for (int groupIndex = 0; groupIndex < reader.RowGroupCount; groupIndex++)
        {
            using ParquetRowGroupReader group = reader.OpenRowGroupReader(groupIndex);

            int rowCount = (int)group.RowCount;

            if (rowCount == 0)
            {
                continue;
            }

            string?[] titles = await ReadStringsAsync(group, fields, DatasetColumns.Title, rowCount, cancellationToken);
            string?[] postedTexts = await ReadStringsAsync(group, fields, DatasetColumns.PostedAt, rowCount, cancellationToken);
            string?[] languages = await ReadStringsAsync(group, fields, DatasetColumns.Language, rowCount, cancellationToken);

            List<int> keptRows = [];
            List<DateTimeOffset?> keptPostedAt = [];

            for (int row = 0; row < rowCount; row++)
            {
                string? title = titles[row];

                if (string.IsNullOrWhiteSpace(title) || !KeepsLanguage(languages[row]))
                {
                    continue;
                }

                DateTimeOffset? postedAt = ParsePostedAt(postedTexts[row]);

                if (postedAt is DateTimeOffset moment && moment < filter.NotBefore)
                {
                    continue;
                }

                if (!filter.TitleRules.Evaluate(title).Passes)
                {
                    continue;
                }

                keptRows.Add(row);
                keptPostedAt.Add(postedAt);
            }

            if (keptRows.Count == 0)
            {
                continue;
            }

            string?[] keptTitles = Project(titles, keptRows);
            string?[] urls = await ReadKeptStringsAsync(group, fields, DatasetColumns.Url, rowCount, keptRows, cancellationToken);
            string?[] companies = await ReadKeptStringsAsync(group, fields, DatasetColumns.Company, rowCount, keptRows, cancellationToken);
            string?[] atsTypes = await ReadKeptStringsAsync(group, fields, DatasetColumns.AtsType, rowCount, keptRows, cancellationToken);
            string?[] atsIds = await ReadKeptStringsAsync(group, fields, DatasetColumns.AtsId, rowCount, keptRows, cancellationToken);
            string?[] locations = await ReadKeptStringsAsync(group, fields, DatasetColumns.Location, rowCount, keptRows, cancellationToken);
            string?[] countries = await ReadKeptStringsAsync(group, fields, DatasetColumns.CountryIso, rowCount, keptRows, cancellationToken);
            string?[] regions = await ReadKeptStringsAsync(group, fields, DatasetColumns.Region, rowCount, keptRows, cancellationToken);
            string?[] currencies = await ReadKeptStringsAsync(group, fields, DatasetColumns.SalaryCurrency, rowCount, keptRows, cancellationToken);
            string?[] periods = await ReadKeptStringsAsync(group, fields, DatasetColumns.SalaryPeriod, rowCount, keptRows, cancellationToken);
            string?[] employmentTypes = await ReadKeptStringsAsync(group, fields, DatasetColumns.EmploymentType, rowCount, keptRows, cancellationToken);
            string?[] departments = await ReadKeptStringsAsync(group, fields, DatasetColumns.Department, rowCount, keptRows, cancellationToken);
            string?[] applyUrls = await ReadKeptStringsAsync(group, fields, DatasetColumns.ApplyUrl, rowCount, keptRows, cancellationToken);
            bool?[] remoteFlags = await ReadKeptBooleansAsync(group, fields, DatasetColumns.IsRemote, rowCount, keptRows, cancellationToken);
            double?[] salaryMinimums = await ReadKeptDoublesAsync(group, fields, DatasetColumns.SalaryMin, rowCount, keptRows, cancellationToken);
            double?[] salaryMaximums = await ReadKeptDoublesAsync(group, fields, DatasetColumns.SalaryMax, rowCount, keptRows, cancellationToken);
            string?[] descriptions = await ReadKeptStringsAsync(group, fields, DatasetColumns.Description, rowCount, keptRows, cancellationToken);

            for (int index = 0; index < keptRows.Count; index++)
            {
                string? url = Clean(urls[index]);

                if (url is null)
                {
                    continue;
                }

                string? atsType = Clean(atsTypes[index]);
                string? department = Clean(departments[index]);

                yield return new RawJob(
                    JobSourceKind.Dataset,
                    BuildSourceId(atsType, Clean(atsIds[index]), url),
                    Clean(keptTitles[index]) ?? string.Empty,
                    Clean(companies[index]) ?? string.Empty,
                    null,
                    url,
                    Clean(applyUrls[index]),
                    descriptions[index] ?? string.Empty,
                    Clean(locations[index]),
                    Clean(countries[index]),
                    Clean(regions[index]),
                    remoteFlags[index],
                    Clean(languages[keptRows[index]]),
                    ToMoney(salaryMinimums[index]),
                    ToMoney(salaryMaximums[index]),
                    Clean(currencies[index]),
                    CompNormalizer.ParsePeriod(periods[index]),
                    null,
                    Clean(employmentTypes[index]),
                    atsType,
                    department is null ? [] : [department],
                    keptPostedAt[index]);
            }
        }
    }

    private static Dictionary<string, DataField> IndexFields(ParquetSchema schema)
    {
        Dictionary<string, DataField> fields = new(StringComparer.OrdinalIgnoreCase);

        foreach (DataField field in schema.DataFields)
        {
            fields[field.Name] = field;
        }

        return fields;
    }

    private static async Task<string?[]> ReadStringsAsync(ParquetRowGroupReader group, Dictionary<string, DataField> fields, string columnName, int rowCount, CancellationToken cancellationToken)
    {
        string?[] values = new string?[rowCount];

        if (fields.TryGetValue(columnName, out DataField? field))
        {
            await group.ReadAsync(field, values.AsMemory(), cancellationToken: cancellationToken);
        }

        return values;
    }

    private static async Task<string?[]> ReadKeptStringsAsync(ParquetRowGroupReader group, Dictionary<string, DataField> fields, string columnName, int rowCount, List<int> keptRows, CancellationToken cancellationToken)
    {
        return Project(await ReadStringsAsync(group, fields, columnName, rowCount, cancellationToken), keptRows);
    }

    private static async Task<bool?[]> ReadKeptBooleansAsync(ParquetRowGroupReader group, Dictionary<string, DataField> fields, string columnName, int rowCount, List<int> keptRows, CancellationToken cancellationToken)
    {
        bool?[] values = new bool?[rowCount];

        if (fields.TryGetValue(columnName, out DataField? field))
        {
            await group.ReadAsync(field, values.AsMemory(), cancellationToken: cancellationToken);
        }

        return Project(values, keptRows);
    }

    private static async Task<double?[]> ReadKeptDoublesAsync(ParquetRowGroupReader group, Dictionary<string, DataField> fields, string columnName, int rowCount, List<int> keptRows, CancellationToken cancellationToken)
    {
        double?[] values = new double?[rowCount];

        if (fields.TryGetValue(columnName, out DataField? field))
        {
            await group.ReadAsync(field, values.AsMemory(), cancellationToken: cancellationToken);
        }

        return Project(values, keptRows);
    }

    private static T[] Project<T>(T[] values, List<int> keptRows)
    {
        T[] kept = new T[keptRows.Count];

        for (int index = 0; index < keptRows.Count; index++)
        {
            kept[index] = values[keptRows[index]];
        }

        return kept;
    }

    private static bool KeepsLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return true;
        }

        ReadOnlySpan<char> value = language.AsSpan().Trim();

        return value.Equals("en", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("en-", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTimeOffset? ParsePostedAt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset postedAt)
            ? postedAt
            : null;
    }

    private static decimal? ToMoney(double? value)
    {
        if (value is not double amount || double.IsNaN(amount) || double.IsInfinity(amount))
        {
            return null;
        }

        return amount is < (double)decimal.MinValue or > (double)decimal.MaxValue ? null : (decimal)amount;
    }

    private static string BuildSourceId(string? atsType, string? atsId, string url)
    {
        if (atsId is null)
        {
            return url;
        }

        return atsType is null ? atsId : $"{atsType}:{atsId}";
    }

    private static string? Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
