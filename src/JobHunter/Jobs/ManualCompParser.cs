using System.Globalization;
using System.Text.RegularExpressions;
using JobHunter.Domain;
using JobHunter.Pipeline;

namespace JobHunter.Jobs;

/// <summary>Compensation as it was typed on the add form, once the figures, the currency and the period have been read out of it.</summary>
public sealed record ManualComp(decimal? Min, decimal? Max, string? Currency, CompPeriod? Period)
{
    /// <summary>Compensation that the text does not state.</summary>
    public static ManualComp Unknown { get; } = new(null, null, null, null);

    /// <summary>True when the text carried no figure at all.</summary>
    public bool IsUnknown => Min is null && Max is null;
}

/// <summary>Reads the compensation out of the free text a manual job carries, which is how it is written in a posting rather than in columns.</summary>
public static partial class ManualCompParser
{
    private static readonly (string Token, string Code)[] CurrencyTokens =
    [
        ("EUR", "EUR"),
        ("€", "EUR"),
        ("USD", "USD"),
        ("$", "USD"),
        ("GBP", "GBP"),
        ("£", "GBP"),
        ("PLN", "PLN"),
        ("CHF", "CHF"),
        ("SEK", "SEK"),
        ("NOK", "NOK"),
        ("DKK", "DKK"),
        ("CZK", "CZK"),
        ("HUF", "HUF"),
        ("CAD", "CAD"),
        ("AUD", "AUD")
    ];

    /// <summary>Reads up to two figures, the currency and the period; text without a figure counts as no compensation stated.</summary>
    public static ManualComp Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ManualComp.Unknown;
        }

        List<decimal> amounts = ReadAmounts(text);
        if (amounts.Count == 0)
        {
            return ManualComp.Unknown;
        }

        decimal min = amounts[0];
        decimal? max = amounts.Count > 1 ? amounts[1] : null;

        if (max is decimal upper && upper < min)
        {
            (min, max) = (upper, min);
        }

        return new ManualComp(min, max, ReadCurrency(text), ReadPeriod(text));
    }

    private static List<decimal> ReadAmounts(string text)
    {
        List<decimal> amounts = [];

        foreach (Match match in Amount().Matches(text))
        {
            if (ReadAmount(match.Groups["number"].Value, match.Groups["thousands"].Success) is decimal amount)
            {
                amounts.Add(amount);
            }

            if (amounts.Count == 2)
            {
                break;
            }
        }

        return amounts;
    }

    private static decimal? ReadAmount(string number, bool thousandsSuffix)
    {
        string cleaned = thousandsSuffix ? number.Replace(',', '.') : WithoutGroupSeparators(number);

        if (!decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value))
        {
            return null;
        }

        return thousandsSuffix ? value * 1000m : value;
    }

    private static string WithoutGroupSeparators(string number)
    {
        int lastSeparator = number.LastIndexOfAny(['.', ',']);
        if (lastSeparator < 0)
        {
            return number;
        }

        string tail = number[(lastSeparator + 1)..];
        string head = number[..lastSeparator].Replace(",", string.Empty, StringComparison.Ordinal).Replace(".", string.Empty, StringComparison.Ordinal);

        return tail.Length == 3 ? head + tail : $"{head}.{tail}";
    }

    private static string? ReadCurrency(string text)
    {
        string upper = text.ToUpperInvariant();

        foreach ((string token, string code) in CurrencyTokens)
        {
            if (upper.Contains(token, StringComparison.Ordinal))
            {
                return code;
            }
        }

        return null;
    }

    private static CompPeriod? ReadPeriod(string text)
    {
        Match match = Period().Match(text);

        return match.Success ? CompNormalizer.ParsePeriod(match.Groups["period"].Value) : null;
    }

    [GeneratedRegex(@"(?<number>\d+(?:[.,]\d+)*)\s*(?<thousands>[kK])?")]
    private static partial Regex Amount();

    [GeneratedRegex(@"\b(?<period>hourly|hour|hr|h|daily|day|monthly|month|mo|yearly|year|yr)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Period();
}
