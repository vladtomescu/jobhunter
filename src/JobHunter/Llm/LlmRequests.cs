using System.Globalization;
using JobHunter.Domain;
using JobHunter.Llm.Contracts;

namespace JobHunter.Llm;

/// <summary>Builds what the model is told about a job, so that the interface path and the exchange files always send the same fields.</summary>
public static class LlmRequests
{
    /// <summary>How much of a description travels with a request; everything past it is cut.</summary>
    public const int DescriptionLimit = 6000;

    /// <summary>Builds the scoring request for one job.</summary>
    public static ScoreRequest ForScore(Job job, string model)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        return new ScoreRequest(
            job.Id,
            job.Title,
            job.Company,
            job.LocationText ?? job.RegionText ?? job.CountryIso,
            RemoteHint(job),
            job.EmploymentTypeFromSource,
            CompensationText(job),
            job.PostedAt,
            [.. job.Flags.Select(flag => flag.ToString())],
            Truncate(job.DescriptionText),
            model);
    }

    /// <summary>Builds the kit request for one job that was chosen for pursuit, carrying the score it was chosen on.</summary>
    public static KitRequest ForKit(Job job, ScorePayload score, string model)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(score);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        return new KitRequest(
            job.Id,
            job.Title,
            job.Company,
            job.ApplyUrl ?? job.PostingUrl,
            Truncate(job.DescriptionText),
            score,
            job.Class?.ToString() ?? string.Empty,
            [.. job.Flags.Select(flag => flag.ToString())],
            job.Language,
            model);
    }

    /// <summary>Cuts a description down to the length a request carries.</summary>
    public static string Truncate(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= DescriptionLimit ? text : text[..DescriptionLimit];
    }

    /// <summary>Says what the source reported about working remotely, in one word.</summary>
    public static string? RemoteHint(Job job)
    {
        ArgumentNullException.ThrowIfNull(job);

        return job.IsRemoteFromSource switch
        {
            true => "remote",
            false => "onsite",
            null => null
        };
    }

    /// <summary>Writes the compensation the source stated the way a posting would phrase it, or null when nothing was stated.</summary>
    public static string? CompensationText(Job job)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (job.CompMin is null && job.CompMax is null)
        {
            return null;
        }

        string amounts = (job.CompMin, job.CompMax) switch
        {
            (decimal min, decimal max) when min != max => $"{Amount(min)}-{Amount(max)}",
            (decimal min, _) => Amount(min),
            (_, decimal max) => Amount(max),
            _ => string.Empty
        };

        string currency = string.IsNullOrWhiteSpace(job.CompCurrency) ? string.Empty : $" {job.CompCurrency.Trim().ToUpperInvariant()}";
        string period = job.CompPeriod is CompPeriod stated ? $" per {stated.ToString().ToLowerInvariant()}" : string.Empty;

        return $"{amounts}{currency}{period}";
    }

    private static string Amount(decimal value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
