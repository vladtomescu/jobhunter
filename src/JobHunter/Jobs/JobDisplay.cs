using System.Globalization;
using JobHunter.Domain;
using JobHunter.Pipeline;

namespace JobHunter.Jobs;

/// <summary>Turns the stored job values into the short strings the pages show: compensation in the base currency a year, the place, the age, local timestamps, and flags worded from the candidate's own settings.</summary>
public static class JobDisplay
{
    /// <summary>What a page shows where a job states no compensation.</summary>
    public const string CompUnknown = "not stated";

    /// <summary>Compensation as the base currency a year, marked approximate because it is converted and annualized.</summary>
    public static string Comp(decimal? minPerYear, decimal? maxPerYear, string baseCurrency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseCurrency);

        if (minPerYear is null && maxPerYear is null)
        {
            return CompUnknown;
        }

        if (minPerYear is decimal low && maxPerYear is decimal high && low != high)
        {
            return $"≈ {Amount(low)} - {Amount(high)} {baseCurrency}/year";
        }

        return $"≈ {Amount(maxPerYear ?? minPerYear!.Value)} {baseCurrency}/year";
    }

    /// <summary>Where the role sits, as far as the posting and the score say. A policy of "unknown" is treated as no policy at all.</summary>
    public static string Place(string? remotePolicy, string? locationText, string? countryIso)
    {
        string? place = First(locationText, countryIso);
        string? policy = string.Equals(remotePolicy?.Trim(), "unknown", StringComparison.OrdinalIgnoreCase) ? null : remotePolicy;

        if (string.IsNullOrWhiteSpace(policy))
        {
            return place ?? "unknown";
        }

        return place is null ? policy : $"{policy}, {place}";
    }

    /// <summary>How long ago the posting appeared, counted from the posted date when there is one.</summary>
    public static string Age(DateTimeOffset? postedAt, DateTimeOffset firstSeenAt)
    {
        int days = (int)Math.Floor((DateTimeOffset.UtcNow - (postedAt ?? firstSeenAt)).TotalDays);

        return days <= 0 ? "today" : $"{days}d";
    }

    /// <summary>A stored timestamp as local time, which is how every date is shown.</summary>
    public static string LocalTime(DateTimeOffset at)
    {
        return at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>A stored timestamp as a local date.</summary>
    public static string LocalDate(DateTimeOffset at)
    {
        return at.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>The short code a chip shows for a flag: the home city, the stack keyword or the high-pay threshold for the settings-driven flags, the stored name for the others.</summary>
    /// <remarks><paramref name="matchedStackKeyword"/> is the keyword this particular job actually matched, when the caller already has the title, tags and description loaded cheaply enough to find it (the job page); a caller working from a lean list row passes null and gets the candidate's first configured keyword instead.</remarks>
    public static string FlagCode(JobFlag flag, Domain.Settings settings, string? matchedStackKeyword = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return flag switch
        {
            JobFlag.StackMatch => matchedStackKeyword ?? FirstStackKeyword(settings) ?? "stack",
            JobFlag.HomeCity => string.IsNullOrWhiteSpace(settings.HomeCity) ? "home" : settings.HomeCity,
            JobFlag.HighPay => HighPayCode(settings.HighPayThresholdPerYear),
            _ => flag.ToString()
        };
    }

    /// <summary>The short word shown after a flag's code on a chip; null when the code says it all. The full meaning goes in the chip's tooltip.</summary>
    public static string? FlagLabel(JobFlag flag)
    {
        return flag switch
        {
            JobFlag.H1 => "senior",
            JobFlag.H2 => "employment only",
            JobFlag.H3 => "outside your regions",
            JobFlag.H4 => "onsite or hybrid",
            JobFlag.WA => "US authorization",
            JobFlag.CU => "pay not stated",
            _ => null
        };
    }

    /// <summary>What a flag stands for, shown on the badge, worded from the candidate's own settings where a value helps.</summary>
    public static string FlagMeaning(JobFlag flag, Domain.Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return flag switch
        {
            JobFlag.H1 => "senior levelled",
            JobFlag.H2 => "employment only",
            JobFlag.H3 => "outside your accepted regions or their working hours",
            JobFlag.H4 => "onsite or hybrid, relocation implied",
            JobFlag.HomeCity => "in your home city",
            JobFlag.WA => "United States work authorization required",
            JobFlag.StackMatch => "mentions one of your stack keywords",
            JobFlag.HighPay => HighPayMeaning(settings.HighPayThresholdPerYear, settings.BaseCurrency),
            _ => "compensation not stated"
        };
    }

    /// <summary>The chip modifier class that colours a flag by what it means; compensation not stated takes the neutral chip, and H2 takes the neutral chip too once it stops counting against the job.</summary>
    public static string? FlagTone(JobFlag flag, Domain.Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return flag switch
        {
            JobFlag.StackMatch => "stack-match",
            JobFlag.HighPay => "high-pay",
            JobFlag.H1 or JobFlag.HomeCity => "good",
            JobFlag.H2 => CountsAgainst(flag, settings) ? "warn" : null,
            JobFlag.H3 or JobFlag.H4 => "warn",
            JobFlag.WA => "bad",
            _ => null
        };
    }

    /// <summary>True for a flag that counts against a job: non-European hours, onsite or hybrid, or United States work authorization required always do; employment only counts against a job only for a candidate who takes contract work, the same rule <c>Classifier.BlocksTheCandidate</c> applies to the "b2b" blocking unknown.</summary>
    public static bool CountsAgainst(JobFlag flag, Domain.Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return flag switch
        {
            JobFlag.H2 => settings.ContractPreference == ContractPreference.Contractor,
            JobFlag.H3 or JobFlag.H4 or JobFlag.WA => true,
            _ => false
        };
    }

    /// <summary>True for a flag that speaks for a job strongly enough to show on the inbox: a stack-keyword match, or pay reaching the high-pay threshold.</summary>
    public static bool IsHighlight(JobFlag flag)
    {
        return flag is JobFlag.StackMatch or JobFlag.HighPay;
    }

    /// <summary>The flags an inbox row shows, in their stored order: the ones that count against the job and the two highlights; senior levelled, home city and compensation not stated repeat what the row already says.</summary>
    public static IReadOnlyList<JobFlag> FlagsShownOnInbox(IEnumerable<JobFlag> flags, Domain.Settings settings)
    {
        ArgumentNullException.ThrowIfNull(flags);
        ArgumentNullException.ThrowIfNull(settings);

        return [.. flags.Where(flag => CountsAgainst(flag, settings) || IsHighlight(flag))];
    }

    private static string? FirstStackKeyword(Domain.Settings settings)
    {
        return LiteralTermPattern.ReadTerms(settings.StackKeywords, ',').FirstOrDefault();
    }

    private static string HighPayCode(decimal? thresholdPerYear)
    {
        if (thresholdPerYear is not decimal threshold)
        {
            return "high pay";
        }

        return threshold >= 1_000m ? $"{Amount(threshold / 1_000m)}k+" : $"{Amount(threshold)}+";
    }

    private static string HighPayMeaning(decimal? thresholdPerYear, string baseCurrency)
    {
        return thresholdPerYear is decimal threshold
            ? $"pays at least {Amount(threshold)} {baseCurrency} a year"
            : "pays at least your configured high-pay threshold a year";
    }

    private static string Amount(decimal value)
    {
        return value.ToString("#,##0", CultureInfo.InvariantCulture);
    }

    private static string? First(string? preferred, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred.Trim();
        }

        return string.IsNullOrWhiteSpace(fallback) ? null : fallback.Trim();
    }
}
