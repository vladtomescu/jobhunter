using System.Text.RegularExpressions;
using JobHunter.Domain;
using JobHunter.Sources;

namespace JobHunter.Pipeline;

/// <summary>Everything the deterministic prefilter is allowed to look at; the two keep switches come from the settings.</summary>
public sealed record PrefilterInput(
    string Title,
    string DescriptionText,
    string? LocationText,
    string? RegionText,
    string? CountryIso,
    bool? IsRemoteFromSource,
    string? Language,
    IReadOnlyList<string> Tags,
    IReadOnlyList<JobSourceKind> Sources,
    bool IsManual,
    DateTimeOffset? PostedAt,
    bool HasStatedComp,
    DateTimeOffset EvaluatedAt,
    bool KeepUsOnlyRemote,
    bool KeepOnsiteWithCompOrRelocation)
{
    /// <summary>Reads the prefilter input off a job and the current settings.</summary>
    /// <remarks>The settings type is written qualified because the JobHunter.Settings namespace shadows the plain name.</remarks>
    public static PrefilterInput FromJob(Job job, Domain.Settings settings, DateTimeOffset evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(settings);

        return new PrefilterInput(
            job.Title,
            job.DescriptionText,
            job.LocationText,
            job.RegionText,
            job.CountryIso,
            job.IsRemoteFromSource,
            job.Language,
            [.. job.Tags],
            [.. job.Sources.Select(source => source.Kind)],
            job.IsManual,
            job.PostedAt,
            job.CompMin is not null || job.CompMax is not null,
            evaluatedAt,
            settings.AcceptUnitedStatesRemote,
            settings.KeepOnsiteWithCompOrRelocation);
    }
}

/// <summary>What the prefilter decided: the state the job moves to, the reason when it is dropped, and the flags raised along the way.</summary>
public sealed record PrefilterVerdict(PrefilterState State, string? DropReason, IReadOnlyList<JobFlag> Flags)
{
    /// <summary>A job that survives the rules, carrying its flags.</summary>
    public static PrefilterVerdict Pass(IReadOnlyList<JobFlag> flags)
    {
        return new PrefilterVerdict(PrefilterState.Passed, null, flags);
    }

    /// <summary>A job the rules drop, with the reason shown in the job list.</summary>
    public static PrefilterVerdict Drop(string reason, IReadOnlyList<JobFlag> flags)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new PrefilterVerdict(PrefilterState.Dropped, reason, flags);
    }
}

/// <summary>The deterministic filter that runs before any model call: it drops what can be judged from the posting alone and flags the rest.</summary>
public sealed partial class Prefilter(ITitleRules titleRules)
{
    /// <summary>A posting older than this many days is out of date.</summary>
    public const int MaximumAgeDays = 45;

    /// <summary>The drop reason for a posting written in a language other than English.</summary>
    public const string LanguageReason = "language is not English";

    /// <summary>The drop reason for a posting that aged out.</summary>
    public const string AgeReason = "posted more than 45 days ago";

    /// <summary>The drop reason for a title that names no engineering work.</summary>
    public const string NoIncludeTermReason = "title carries no engineering term";

    /// <summary>The drop reason for a posting that hides the end client behind an agency.</summary>
    public const string AgencyReason = "agency or bench posting";

    /// <summary>Judges one posting: manual jobs keep their flags and never drop, every other job runs the full rule set.</summary>
    public PrefilterVerdict Evaluate(PrefilterInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        GeographyVerdict geography = GeographyRules.Evaluate(input);
        List<JobFlag> flags = [.. ReadFlags(input), .. geography.Flags];

        if (input.IsManual)
        {
            return PrefilterVerdict.Pass(flags);
        }

        string? reason = FindDropReason(input, geography);

        return reason is null ? PrefilterVerdict.Pass(flags) : PrefilterVerdict.Drop(reason, flags);
    }

    /// <summary>True when the posting hides the end client behind an agency or offers a seat on a bench.</summary>
    public static bool ReadsAsAgencyPosting(string text)
    {
        return !string.IsNullOrWhiteSpace(text) && AgencyPhrase().IsMatch(text);
    }

    /// <summary>True when the posting offers employment only, with no business-to-business contract.</summary>
    public static bool OffersEmploymentOnly(string text)
    {
        return !string.IsNullOrWhiteSpace(text) && EmploymentOnlyPhrase().IsMatch(text);
    }

    private string? FindDropReason(PrefilterInput input, GeographyVerdict geography)
    {
        if (!SpeaksAcceptedLanguage(input.Language))
        {
            return LanguageReason;
        }

        if (input.PostedAt is DateTimeOffset postedAt && input.EvaluatedAt - postedAt > TimeSpan.FromDays(MaximumAgeDays))
        {
            return AgeReason;
        }

        TitleVerdict title = titleRules.Evaluate(input.Title);
        if (title.Kind == TitleVerdictKind.ExcludedByRule)
        {
            return $"title excluded: {title.Reason}";
        }

        if (title.Kind == TitleVerdictKind.NoIncludeTerm)
        {
            return NoIncludeTermReason;
        }

        if (ReadsAsAgencyPosting(input.Title) || ReadsAsAgencyPosting(input.DescriptionText))
        {
            return AgencyReason;
        }

        return geography.Keeps ? null : geography.DropReason;
    }

    private static IEnumerable<JobFlag> ReadFlags(PrefilterInput input)
    {
        if (TitleRules.IsSeniorLevelled(input.Title))
        {
            yield return JobFlag.H1;
        }

        if (OffersEmploymentOnly(input.DescriptionText))
        {
            yield return JobFlag.H2;
        }

        if (!input.HasStatedComp)
        {
            yield return JobFlag.CU;
        }
    }

    private static bool SpeaksAcceptedLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return true;
        }

        string code = language.Trim()[..Math.Min(2, language.Trim().Length)];

        return code.Equals("en", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"\b(our\s+client|one\s+of\s+our\s+clients|a\s+client\s+of\s+ours|client\s+of\s+ours|rotating\s+projects?|rotate\s+between\s+projects|undisclosed\s+client|confidential\s+client|on\s+the\s+bench|body\s+leasing|staffing\s+agency)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AgencyPhrase();

    [GeneratedRegex(@"(\bno\s+(b2b|b\s*2\s*b|contractors?|contracting|freelancers?|corp[\s\-]?to[\s\-]?corp|c2c)\b|\b(employment|permanent)\s+contract\s+only\b|\bfull[\s\-]?time\s+employment\s+only\b|\bemployment\s+only\b|\bw2\s+only\b|\bemployees?\s+only\b|\bpermanent\s+(role|position|employment)\s+only\b)", RegexOptions.IgnoreCase)]
    private static partial Regex EmploymentOnlyPhrase();
}
