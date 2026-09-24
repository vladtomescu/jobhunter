using JobHunter.Domain;

namespace JobHunter.Pipeline;

/// <summary>Compensation expressed the one way the whole application compares it: the base currency per year, rounded to whole units and shown with a tilde.</summary>
public sealed record YearlyComp(decimal? MinPerYear, decimal? MaxPerYear)
{
    /// <summary>Compensation that could not be normalized, either because none was stated or because the currency is unknown.</summary>
    public static YearlyComp Unknown { get; } = new(null, null);

    /// <summary>True when neither bound is known.</summary>
    public bool IsUnknown => MinPerYear is null && MaxPerYear is null;

    /// <summary>The bound used when one figure has to stand for the posting: the upper one when it is stated, the lower one otherwise.</summary>
    public decimal? Headline => MaxPerYear ?? MinPerYear;
}

/// <summary>Turns compensation as a posting states it into the base currency per year, converting the currency through the daily reference rates.</summary>
public sealed class CompNormalizer(IFxRateProvider fxRates)
{
    /// <summary>Working hours in a year, the factor an hourly rate is annualized with.</summary>
    public const decimal HoursPerYear = 1760m;

    /// <summary>Working days in a year, the factor a daily rate is annualized with.</summary>
    public const decimal DaysPerYear = 220m;

    /// <summary>Months in a year.</summary>
    public const decimal MonthsPerYear = 12m;

    /// <summary>Annualizes the stated compensation and converts it into the base currency; a posting that names no currency is read as already in the base currency, and an unknown currency leaves both bounds unknown.</summary>
    /// <remarks>The settings type is written qualified because the JobHunter.Settings namespace shadows the plain name.</remarks>
    public async Task<YearlyComp> ToBasePerYearAsync(decimal? min, decimal? max, string? currency, CompPeriod? period, string baseCurrency, Domain.Settings settings, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseCurrency);
        ArgumentNullException.ThrowIfNull(settings);

        if (min is null && max is null)
        {
            return YearlyComp.Unknown;
        }

        string code = string.IsNullOrWhiteSpace(currency) ? baseCurrency : currency;
        decimal? unitsPerBase = await fxRates.GetUnitsPerBaseAsync(code, baseCurrency, settings, cancellationToken);

        if (unitsPerBase is not decimal rate || rate <= 0m)
        {
            return YearlyComp.Unknown;
        }

        decimal factor = PeriodsPerYear(period) / rate;

        return new YearlyComp(Annualize(min, factor), Annualize(max, factor));
    }

    /// <summary>How many of a period fit in a year; a posting that states no period is read as yearly.</summary>
    public static decimal PeriodsPerYear(CompPeriod? period)
    {
        return period switch
        {
            CompPeriod.Hour => HoursPerYear,
            CompPeriod.Day => DaysPerYear,
            CompPeriod.Month => MonthsPerYear,
            _ => 1m
        };
    }

    /// <summary>Reads the period a posting names, for instance the dataset's salary period column.</summary>
    public static CompPeriod? ParsePeriod(string? period)
    {
        return period?.Trim().ToUpperInvariant() switch
        {
            "HOUR" or "HOURLY" or "HOURS" or "H" or "HR" => CompPeriod.Hour,
            "DAY" or "DAILY" or "DAYS" or "D" => CompPeriod.Day,
            "MONTH" or "MONTHLY" or "MONTHS" or "M" or "MO" => CompPeriod.Month,
            "YEAR" or "YEARLY" or "ANNUAL" or "ANNUALLY" or "YEARS" or "Y" or "YR" => CompPeriod.Year,
            _ => null
        };
    }

    private static decimal? Annualize(decimal? amount, decimal factor)
    {
        return amount is decimal value ? decimal.Round(value * factor, 0, MidpointRounding.AwayFromZero) : null;
    }
}
