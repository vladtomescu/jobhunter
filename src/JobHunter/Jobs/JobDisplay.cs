using System.Globalization;
using JobHunter.Domain;

namespace JobHunter.Jobs;

/// <summary>Turns the stored job values into the short strings the pages show: compensation in euro a year, the place, the age and local timestamps.</summary>
public static class JobDisplay
{
    /// <summary>What a page shows where a job states no compensation.</summary>
    public const string CompUnknown = "not stated";

    /// <summary>Compensation as euro a year, marked approximate because it is converted and annualized.</summary>
    public static string Comp(decimal? minEurYear, decimal? maxEurYear)
    {
        if (minEurYear is null && maxEurYear is null)
        {
            return CompUnknown;
        }

        if (minEurYear is decimal low && maxEurYear is decimal high && low != high)
        {
            return $"≈ {Amount(low)} - {Amount(high)} EUR/year";
        }

        return $"≈ {Amount(maxEurYear ?? minEurYear!.Value)} EUR/year";
    }

    /// <summary>Where the role sits, as far as the posting and the score say.</summary>
    public static string Place(string? remotePolicy, string? locationText, string? countryIso)
    {
        string? place = First(locationText, countryIso);

        if (string.IsNullOrWhiteSpace(remotePolicy))
        {
            return place ?? "unknown";
        }

        return place is null ? remotePolicy : $"{remotePolicy}, {place}";
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

    /// <summary>The short code a chip shows for a flag: the high-pay threshold for the highlight, the stored name for the others.</summary>
    public static string FlagCode(JobFlag flag)
    {
        return flag switch
        {
            JobFlag.HighPay => $"{Amount(Job.HighPayThresholdEurYear / 1000m)}k+",
            _ => flag.ToString()
        };
    }

    /// <summary>What a flag stands for, shown on the badge.</summary>
    public static string FlagMeaning(JobFlag flag)
    {
        return flag switch
        {
            JobFlag.H1 => "senior levelled",
            JobFlag.H2 => "employment only",
            JobFlag.H3 => "United States or other non-European hours",
            JobFlag.H4 => "onsite or hybrid, relocation implied",
            JobFlag.WA => "United States work authorization required",
            JobFlag.HighPay => $"pays at least {Amount(Job.HighPayThresholdEurYear)} EUR a year",
            _ => "compensation not stated"
        };
    }

    /// <summary>The chip modifier class that colours a flag by what it means; compensation not stated takes the neutral chip.</summary>
    public static string? FlagTone(JobFlag flag)
    {
        return flag switch
        {
            JobFlag.HighPay => "high-pay",
            JobFlag.H1 => "good",
            JobFlag.H2 or JobFlag.H3 or JobFlag.H4 => "warn",
            JobFlag.WA => "bad",
            _ => null
        };
    }

    /// <summary>True for a flag that counts against a job: employment only, non-European hours, onsite or hybrid, or United States work authorization required.</summary>
    public static bool CountsAgainst(JobFlag flag)
    {
        return flag is JobFlag.H2 or JobFlag.H3 or JobFlag.H4 or JobFlag.WA;
    }

    /// <summary>True for a flag that speaks for a job strongly enough to show on the inbox: pay reaching the high-pay threshold.</summary>
    public static bool IsHighlight(JobFlag flag)
    {
        return flag is JobFlag.HighPay;
    }

    /// <summary>The flags an inbox row shows, in their stored order: the ones that count against the job and the high-pay highlight; senior levelled and compensation not stated repeat what the row already says.</summary>
    public static IReadOnlyList<JobFlag> FlagsShownOnInbox(IEnumerable<JobFlag> flags)
    {
        ArgumentNullException.ThrowIfNull(flags);

        return [.. flags.Where(flag => CountsAgainst(flag) || IsHighlight(flag))];
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
