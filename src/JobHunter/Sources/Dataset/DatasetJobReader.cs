using System.Globalization;
using System.Runtime.CompilerServices;
using JobHunter.Domain;
using JobHunter.Pipeline;
using Parquet;
using Parquet.Schema;

namespace JobHunter.Sources.Dataset;

/// <summary>What the reader keeps out of a slice: postings no older than the intake window, in a language the candidate accepts, whose title passes the candidate's title rules.</summary>
public sealed record DatasetReadFilter(DateTimeOffset NotBefore, CandidateProfile Candidate);

/// <summary>Streams postings out of one parquet slice one row group at a time, decoding only the columns the requirements name and only for the rows that survive the filter.</summary>
/// <remarks>Each column is decoded by the type the slice actually stores and converted afterwards, because the same column arrives as text in one applicant tracking system and as a number or a flag in another, and a fixed expectation loses the whole slice.</remarks>
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

            object?[] titles = await ReadColumnAsync(group, fields, DatasetColumns.Title, rowCount, cancellationToken);
            object?[] postedStamps = await ReadColumnAsync(group, fields, DatasetColumns.PostedAt, rowCount, cancellationToken);
            object?[] languages = await ReadColumnAsync(group, fields, DatasetColumns.Language, rowCount, cancellationToken);

            List<int> keptRows = [];
            List<DateTimeOffset?> keptPostedAt = [];

            for (int row = 0; row < rowCount; row++)
            {
                string? title = ToText(titles[row]);

                if (title is null || !filter.Candidate.AcceptsPostingLanguage(ToText(languages[row])))
                {
                    continue;
                }

                DateTimeOffset? postedAt = ToMoment(postedStamps[row]);

                if (postedAt is DateTimeOffset moment && moment < filter.NotBefore)
                {
                    continue;
                }

                if (!filter.Candidate.TitleRules.Evaluate(title).Passes)
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

            object?[] keptTitles = Project(titles, keptRows);
            object?[] urls = await ReadKeptColumnAsync(group, fields, DatasetColumns.Url, rowCount, keptRows, cancellationToken);
            object?[] companies = await ReadKeptColumnAsync(group, fields, DatasetColumns.Company, rowCount, keptRows, cancellationToken);
            object?[] atsTypes = await ReadKeptColumnAsync(group, fields, DatasetColumns.AtsType, rowCount, keptRows, cancellationToken);
            object?[] atsIds = await ReadKeptColumnAsync(group, fields, DatasetColumns.AtsId, rowCount, keptRows, cancellationToken);
            object?[] locations = await ReadKeptColumnAsync(group, fields, DatasetColumns.Location, rowCount, keptRows, cancellationToken);
            object?[] countries = await ReadKeptColumnAsync(group, fields, DatasetColumns.CountryIso, rowCount, keptRows, cancellationToken);
            object?[] regions = await ReadKeptColumnAsync(group, fields, DatasetColumns.Region, rowCount, keptRows, cancellationToken);
            object?[] currencies = await ReadKeptColumnAsync(group, fields, DatasetColumns.SalaryCurrency, rowCount, keptRows, cancellationToken);
            object?[] periods = await ReadKeptColumnAsync(group, fields, DatasetColumns.SalaryPeriod, rowCount, keptRows, cancellationToken);
            object?[] employmentTypes = await ReadKeptColumnAsync(group, fields, DatasetColumns.EmploymentType, rowCount, keptRows, cancellationToken);
            object?[] departments = await ReadKeptColumnAsync(group, fields, DatasetColumns.Department, rowCount, keptRows, cancellationToken);
            object?[] applyUrls = await ReadKeptColumnAsync(group, fields, DatasetColumns.ApplyUrl, rowCount, keptRows, cancellationToken);
            object?[] remoteFlags = await ReadKeptColumnAsync(group, fields, DatasetColumns.IsRemote, rowCount, keptRows, cancellationToken);
            object?[] salaryMinimums = await ReadKeptColumnAsync(group, fields, DatasetColumns.SalaryMin, rowCount, keptRows, cancellationToken);
            object?[] salaryMaximums = await ReadKeptColumnAsync(group, fields, DatasetColumns.SalaryMax, rowCount, keptRows, cancellationToken);
            object?[] descriptions = await ReadKeptColumnAsync(group, fields, DatasetColumns.Description, rowCount, keptRows, cancellationToken);

            for (int index = 0; index < keptRows.Count; index++)
            {
                string? url = ToText(urls[index]);

                if (url is null)
                {
                    continue;
                }

                string? atsType = ToText(atsTypes[index]);
                string? department = ToText(departments[index]);

                yield return new RawJob(
                    JobSourceKind.Dataset,
                    BuildSourceId(atsType, ToText(atsIds[index]), url),
                    ToText(keptTitles[index]) ?? string.Empty,
                    ToText(companies[index]) ?? string.Empty,
                    null,
                    url,
                    ToText(applyUrls[index]),
                    ToText(descriptions[index]) ?? string.Empty,
                    ToText(locations[index]),
                    ToText(countries[index]),
                    ToText(regions[index]),
                    ToFlag(remoteFlags[index]),
                    ToText(languages[keptRows[index]]),
                    ToMoney(salaryMinimums[index]),
                    ToMoney(salaryMaximums[index]),
                    ToText(currencies[index]),
                    CompNormalizer.ParsePeriod(ToText(periods[index])),
                    null,
                    ToText(employmentTypes[index]),
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

    /// <summary>Reads one column of the row group as loosely typed values; a column the slice does not carry, or carries in a type nothing maps to, reads as nulls instead of failing the slice.</summary>
    /// <remarks>Text columns report their type as a character buffer, which the string overload of the row group reader decodes.</remarks>
    private static async Task<object?[]> ReadColumnAsync(ParquetRowGroupReader group, Dictionary<string, DataField> fields, string columnName, int rowCount, CancellationToken cancellationToken)
    {
        if (!fields.TryGetValue(columnName, out DataField? field))
        {
            return new object?[rowCount];
        }

        if (field.ClrType == typeof(string) || field.ClrType == typeof(ReadOnlyMemory<char>))
        {
            string?[] text = new string?[rowCount];
            await group.ReadAsync(field, text.AsMemory(), cancellationToken: cancellationToken);

            return text;
        }

        if (field.ClrType == typeof(bool))
        {
            return await ReadValuesAsync<bool>(group, field, rowCount, cancellationToken);
        }

        if (field.ClrType == typeof(long))
        {
            return await ReadValuesAsync<long>(group, field, rowCount, cancellationToken);
        }

        if (field.ClrType == typeof(int))
        {
            return await ReadValuesAsync<int>(group, field, rowCount, cancellationToken);
        }

        if (field.ClrType == typeof(double))
        {
            return await ReadValuesAsync<double>(group, field, rowCount, cancellationToken);
        }

        if (field.ClrType == typeof(float))
        {
            return await ReadValuesAsync<float>(group, field, rowCount, cancellationToken);
        }

        if (field.ClrType == typeof(decimal))
        {
            return await ReadValuesAsync<decimal>(group, field, rowCount, cancellationToken);
        }

        if (field.ClrType == typeof(DateTime))
        {
            return await ReadValuesAsync<DateTime>(group, field, rowCount, cancellationToken);
        }

        return field.ClrType == typeof(DateTimeOffset)
            ? await ReadValuesAsync<DateTimeOffset>(group, field, rowCount, cancellationToken)
            : new object?[rowCount];
    }

    private static async Task<object?[]> ReadKeptColumnAsync(ParquetRowGroupReader group, Dictionary<string, DataField> fields, string columnName, int rowCount, List<int> keptRows, CancellationToken cancellationToken)
    {
        return Project(await ReadColumnAsync(group, fields, columnName, rowCount, cancellationToken), keptRows);
    }

    /// <summary>Reads a column of values, taking the buffer shape from whether the column admits nulls.</summary>
    private static async Task<object?[]> ReadValuesAsync<T>(ParquetRowGroupReader group, DataField field, int rowCount, CancellationToken cancellationToken)
        where T : struct
    {
        object?[] boxed = new object?[rowCount];

        if (field.IsNullable)
        {
            T?[] values = new T?[rowCount];
            await group.ReadAsync(field, values.AsMemory(), cancellationToken: cancellationToken);

            for (int index = 0; index < rowCount; index++)
            {
                boxed[index] = values[index];
            }

            return boxed;
        }

        T[] required = new T[rowCount];
        await group.ReadAsync(field, required.AsMemory(), cancellationToken: cancellationToken);

        for (int index = 0; index < rowCount; index++)
        {
            boxed[index] = required[index];
        }

        return boxed;
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

    /// <summary>The text of a value whatever type the slice stored it as; numbers are written in the invariant culture so that an identifier reads the same everywhere.</summary>
    private static string? ToText(object? value)
    {
        return value switch
        {
            null => null,
            string text => Clean(text),
            bool flag => flag ? "true" : "false",
            IFormattable number => Clean(number.ToString(null, CultureInfo.InvariantCulture)),
            _ => Clean(value.ToString())
        };
    }

    /// <summary>A pay figure, parsed with the invariant culture when the slice stored it as text; anything unreadable counts as not stated.</summary>
    private static decimal? ToMoney(object? value)
    {
        return value switch
        {
            null => null,
            decimal amount => amount,
            double amount => FromDouble(amount),
            float amount => FromDouble(amount),
            long amount => amount,
            int amount => amount,
            string text => decimal.TryParse(text.Trim(), NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out decimal parsed) ? parsed : null,
            _ => null
        };
    }

    /// <summary>The remote flag, whether the slice stored it as a flag, as a number or as text.</summary>
    private static bool? ToFlag(object? value)
    {
        return value switch
        {
            null => null,
            bool flag => flag,
            long number => number != 0,
            int number => number != 0,
            double number => number != 0,
            string text => ParseFlag(text),
            _ => null
        };
    }

    /// <summary>The posting date, whether the slice stored it as a timestamp or as text in one of the formats the dataset mixes.</summary>
    private static DateTimeOffset? ToMoment(object? value)
    {
        return value switch
        {
            null => null,
            DateTimeOffset moment => moment.ToUniversalTime(),
            DateTime moment => new DateTimeOffset(moment.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(moment, DateTimeKind.Utc) : moment.ToUniversalTime()),
            string text => ParseMoment(text),
            _ => null
        };
    }

    private static bool? ParseFlag(string value)
    {
        string text = value.Trim();

        if (bool.TryParse(text, out bool flag))
        {
            return flag;
        }

        return text switch
        {
            "1" => true,
            "0" => false,
            _ => null
        };
    }

    private static decimal? FromDouble(double amount)
    {
        if (double.IsNaN(amount) || double.IsInfinity(amount))
        {
            return null;
        }

        return amount is < (double)decimal.MinValue or > (double)decimal.MaxValue ? null : (decimal)amount;
    }

    private static DateTimeOffset? ParseMoment(string value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset postedAt)
            ? postedAt
            : null;
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
