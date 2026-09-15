using JobHunter.Domain;

namespace JobHunter.Pipeline;

/// <summary>Compensation expressed the one way the whole application compares it: euro per year, rounded to whole euro and shown with a tilde.</summary>
public sealed record EurYearComp(decimal? MinEurYear, decimal? MaxEurYear)
{
    /// <summary>Compensation that could not be normalized, either because none was stated or because the currency is unknown.</summary>
    public static EurYearComp Unknown { get; } = new(null, null);

    /// <summary>True when neither bound is known.</summary>
    public bool IsUnknown => MinEurYear is null && MaxEurYear is null;

    /// <summary>The bound used when one figure has to stand for the posting: the upper one when it is stated, the lower one otherwise.</summary>
    public decimal? Headline => MaxEurYear ?? MinEurYear;
}

/// <summary>Turns compensation as a posting states it into euro per year, converting the currency through the daily reference rates.</summary>
public sealed class CompNormalizer(IFxRateProvider fxRates)
{
    /// <summary>Working hours in a year, the factor an hourly rate is annualized with.</summary>
    public const decimal HoursPerYear = 1760m;

    /// <summary>Working days in a year, the factor a daily rate is annualized with.</summary>
    public const decimal DaysPerYear = 220m;

    /// <summary>Months in a year.</summary>
    public const decimal MonthsPerYear = 12m;

    private const string DefaultCurrency = "EUR";

    /// <summary>Annualizes and converts the stated compensation; an unknown currency leaves both bounds unknown.</summary>
    /// <remarks>The settings type is written qualified because the JobHunter.Settings namespace shadows the plain name.</remarks>
    public async Task<EurYearComp> ToEurPerYearAsync(decimal? min, decimal? max, string? currency, CompPeriod? period, Domain.Settings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (min is null && max is null)
        {
            return EurYearComp.Unknown;
        }

        string code = string.IsNullOrWhiteSpace(currency) ? DefaultCurrency : currency;
        decimal? unitsPerEuro = await fxRates.GetUnitsPerEuroAsync(code, settings, cancellationToken);

        if (unitsPerEuro is not decimal rate || rate <= 0m)
        {
            return EurYearComp.Unknown;
        }

        decimal factor = PeriodsPerYear(period) / rate;

        return new EurYearComp(Annualize(min, factor), Annualize(max, factor));
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
