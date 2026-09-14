using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace JobHunter.Data;

/// <summary>The storage conversions SQLite needs: it stores decimals and timestamps as text by default and then refuses to order or range-compare them in SQL.</summary>
public static class SqliteStorageConversions
{
    /// <summary>The fixed-width invariant shape every stored timestamp takes, so that comparing the text in SQL compares the instants.</summary>
    public const string UtcTimestampFormat = "yyyy-MM-dd HH:mm:ss.fffffff";

    private const DateTimeStyles UtcTimestampStyles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;

    /// <summary>Stores a decimal in a REAL column while the model keeps the decimal type, so comparisons and ordering happen in SQL.</summary>
    public static ValueConverter<decimal, double> DecimalToDouble { get; } = new(
        value => (double)value,
        stored => (decimal)stored);

    /// <summary>Stores a timestamp as fixed-width invariant text in UTC and reads it back as a UTC value with a zero offset.</summary>
    public static ValueConverter<DateTimeOffset, string> DateTimeOffsetToUtcText { get; } = new(
        value => value.ToUniversalTime().ToString(UtcTimestampFormat, CultureInfo.InvariantCulture),
        stored => DateTimeOffset.ParseExact(stored, UtcTimestampFormat, CultureInfo.InvariantCulture, UtcTimestampStyles));
}
