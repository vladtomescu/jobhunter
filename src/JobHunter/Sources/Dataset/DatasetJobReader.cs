using System.Globalization;
using JobHunter.Domain;
using JobHunter.Pipeline;
using ParquetSharp;

namespace JobHunter.Sources.Dataset;

/// <summary>What the reader keeps out of a slice: postings no older than the intake window, in a language the candidate accepts, whose title passes the candidate's title rules.</summary>
public sealed record DatasetReadFilter(DateTimeOffset NotBefore, CandidateProfile Candidate);

/// <summary>Streams postings out of one parquet slice one row group at a time, decoding only the columns the requirements name and only for the rows that survive the filter.</summary>
/// <remarks>Each column is decoded by the type the slice actually stores and converted afterwards, because the same column arrives as text in one applicant tracking system and as a number or a flag in another, and a fixed expectation loses the whole slice.</remarks>
/// <remarks>A slice stores all its rows in one or two row groups, and the descriptions of one group reach a gigabyte of text. The columns the filter reads are decoded in small batches, and every other column is read for the surviving rows alone, so the reader never holds more than a few pages of a column.</remarks>
public sealed class DatasetJobReader
{
    private const int BatchSize = 1024;

    /// <summary>Yields one posting per surviving row; neither the slice nor a whole column of it is ever held in memory.</summary>
    public IEnumerable<RawJob> Read(string parquetFilePath, DatasetReadFilter filter, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parquetFilePath);
        ArgumentNullException.ThrowIfNull(filter);

        using ParquetFileReader file = new(parquetFilePath);

        Dictionary<string, int> columns = IndexColumns(file.FileMetaData.Schema);

        if (!columns.ContainsKey(DatasetColumns.Url) || !columns.ContainsKey(DatasetColumns.Title))
        {
            throw new InvalidOperationException($"The slice at {parquetFilePath} carries neither a {DatasetColumns.Url} nor a {DatasetColumns.Title} column, so no posting can be identified.");
        }

        for (int groupIndex = 0; groupIndex < file.FileMetaData.NumRowGroups; groupIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using RowGroupReader group = file.RowGroup(groupIndex);

            int rowCount = (int)group.MetaData.NumRows;

            if (rowCount == 0)
            {
                continue;
            }

            object?[] titles = ReadColumn(group, columns, DatasetColumns.Title, rowCount);
            object?[] postedStamps = ReadColumn(group, columns, DatasetColumns.PostedAt, rowCount);
            object?[] languages = ReadColumn(group, columns, DatasetColumns.Language, rowCount);

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

            using ColumnCursor urls = OpenColumn(group, columns, DatasetColumns.Url);
            using ColumnCursor companies = OpenColumn(group, columns, DatasetColumns.Company);
            using ColumnCursor atsTypes = OpenColumn(group, columns, DatasetColumns.AtsType);
            using ColumnCursor atsIds = OpenColumn(group, columns, DatasetColumns.AtsId);
            using ColumnCursor locations = OpenColumn(group, columns, DatasetColumns.Location);
            using ColumnCursor countries = OpenColumn(group, columns, DatasetColumns.CountryIso);
            using ColumnCursor regions = OpenColumn(group, columns, DatasetColumns.Region);
            using ColumnCursor currencies = OpenColumn(group, columns, DatasetColumns.SalaryCurrency);
            using ColumnCursor periods = OpenColumn(group, columns, DatasetColumns.SalaryPeriod);
            using ColumnCursor employmentTypes = OpenColumn(group, columns, DatasetColumns.EmploymentType);
            using ColumnCursor departments = OpenColumn(group, columns, DatasetColumns.Department);
            using ColumnCursor applyUrls = OpenColumn(group, columns, DatasetColumns.ApplyUrl);
            using ColumnCursor remoteFlags = OpenColumn(group, columns, DatasetColumns.IsRemote);
            using ColumnCursor salaryMinimums = OpenColumn(group, columns, DatasetColumns.SalaryMin);
            using ColumnCursor salaryMaximums = OpenColumn(group, columns, DatasetColumns.SalaryMax);
            using ColumnCursor descriptions = OpenColumn(group, columns, DatasetColumns.Description);

            for (int index = 0; index < keptRows.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int row = keptRows[index];
                string? url = ToText(urls.ReadAt(row));

                if (url is null)
                {
                    continue;
                }

                string? atsType = ToText(atsTypes.ReadAt(row));
                string? department = ToText(departments.ReadAt(row));

                yield return new RawJob(
                    JobSourceKind.Dataset,
                    BuildSourceId(atsType, ToText(atsIds.ReadAt(row)), url),
                    ToText(titles[row]) ?? string.Empty,
                    ToText(companies.ReadAt(row)) ?? string.Empty,
                    null,
                    url,
                    ToText(applyUrls.ReadAt(row)),
                    ToText(descriptions.ReadAt(row)) ?? string.Empty,
                    ToText(locations.ReadAt(row)),
                    ToText(countries.ReadAt(row)),
                    ToText(regions.ReadAt(row)),
                    ToFlag(remoteFlags.ReadAt(row)),
                    ToText(languages[row]),
                    ToMoney(salaryMinimums.ReadAt(row)),
                    ToMoney(salaryMaximums.ReadAt(row)),
                    ToText(currencies.ReadAt(row)),
                    CompNormalizer.ParsePeriod(ToText(periods.ReadAt(row))),
                    null,
                    ToText(employmentTypes.ReadAt(row)),
                    atsType,
                    department is null ? [] : [department],
                    keptPostedAt[index]);
            }
        }
    }

    private static Dictionary<string, int> IndexColumns(SchemaDescriptor schema)
    {
        Dictionary<string, int> columns = new(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < schema.NumColumns; index++)
        {
            columns[schema.Column(index).Name] = index;
        }

        return columns;
    }

    /// <summary>Reads one whole column of the row group as loosely typed values; only the short columns the filter needs are read this way.</summary>
    private static object?[] ReadColumn(RowGroupReader group, Dictionary<string, int> columns, string columnName, int rowCount)
    {
        using ColumnCursor cursor = OpenColumn(group, columns, columnName);

        return cursor.ReadAll(rowCount);
    }

    /// <summary>Opens one column of the row group; a column the slice does not carry, or carries in a type nothing maps to, reads as nulls instead of failing the slice.</summary>
    private static ColumnCursor OpenColumn(RowGroupReader group, Dictionary<string, int> columns, string columnName)
    {
        if (!columns.TryGetValue(columnName, out int columnIndex))
        {
            return new AbsentColumn();
        }

        ColumnReader column = group.Column(columnIndex);

        return column.LogicalReader(BatchSize).Apply(new CursorFactory(column));
    }

    /// <summary>Whether a column stored as this type maps to something a posting uses.</summary>
    private static bool IsReadable(Type storedType)
    {
        Type valueType = Nullable.GetUnderlyingType(storedType) ?? storedType;

        return valueType == typeof(string)
            || valueType == typeof(bool)
            || valueType == typeof(long)
            || valueType == typeof(int)
            || valueType == typeof(double)
            || valueType == typeof(float)
            || valueType == typeof(decimal)
            || valueType == typeof(DateTime);
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

    /// <summary>One column of a row group, read forwards only.</summary>
    private abstract class ColumnCursor : IDisposable
    {
        /// <summary>Reads every value of the column, from its start.</summary>
        public abstract object?[] ReadAll(int rowCount);

        /// <summary>Reads the value of one row and skips the rows before it without decoding them; rows must be asked for in rising order.</summary>
        public abstract object? ReadAt(int row);

        public virtual void Dispose()
        {
        }
    }

    /// <summary>A column the slice does not carry in a readable form: every row reads as null.</summary>
    private sealed class AbsentColumn : ColumnCursor
    {
        public override object?[] ReadAll(int rowCount)
        {
            return new object?[rowCount];
        }

        public override object? ReadAt(int row)
        {
            return null;
        }
    }

    private sealed class StoredColumn<TValue>(ColumnReader column, LogicalColumnReader<TValue> reader) : ColumnCursor
    {
        private readonly TValue[] single = new TValue[1];

        private int position;

        public override object?[] ReadAll(int rowCount)
        {
            object?[] values = new object?[rowCount];
            TValue[] batch = new TValue[BatchSize];
            int filled = 0;

            while (filled < rowCount && reader.HasNext)
            {
                int read = reader.ReadBatch(batch.AsSpan(0, Math.Min(BatchSize, rowCount - filled)));

                for (int index = 0; index < read; index++)
                {
                    values[filled++] = batch[index];
                }
            }

            return values;
        }

        public override object? ReadAt(int row)
        {
            if (row > position)
            {
                position += (int)reader.Skip(row - position);
            }

            reader.ReadBatch(single);
            position++;

            return single[0];
        }

        public override void Dispose()
        {
            reader.Dispose();
            column.Dispose();
        }
    }

    /// <summary>Builds the cursor for a column from the type the slice stores it as.</summary>
    private sealed class CursorFactory(ColumnReader column) : ILogicalColumnReaderVisitor<ColumnCursor>
    {
        public ColumnCursor OnLogicalColumnReader<TValue>(LogicalColumnReader<TValue> columnReader)
        {
            if (IsReadable(typeof(TValue)))
            {
                return new StoredColumn<TValue>(column, columnReader);
            }

            columnReader.Dispose();
            column.Dispose();

            return new AbsentColumn();
        }
    }
}
