using System.Text.RegularExpressions;
using FuzzySharp;

namespace JobHunter.Pipeline;

/// <summary>A job already known, reduced to what deduplication compares.</summary>
public sealed record DuplicateCandidate(Guid JobId, string Company, string Title, DateTimeOffset FirstSeenAt);

/// <summary>Recognizes the same posting under a second URL: same company, near-identical title, sighted inside the window that follows the first sighting.</summary>
public static partial class DuplicateMatcher
{
    /// <summary>The similarity a pair of titles needs to count as the same job.</summary>
    public const int MinimumSimilarity = 92;

    /// <summary>How many days after the first sighting a fuzzy match is still believed.</summary>
    public const int WindowDays = 30;

    private static readonly string[] LegalSuffixes =
    [
        "inc", "llc", "ltd", "limited", "gmbh", "mbh", "bv", "nv", "sa", "sas", "srl", "sro", "ag", "oy", "oyj", "ab",
        "as", "aps", "plc", "corp", "corporation", "co", "company", "spa", "kft", "doo", "pte", "pty", "the"
    ];

    /// <summary>Reduces a company name to the form two feeds are likely to agree on.</summary>
    public static string NormalizeCompany(string company)
    {
        ArgumentNullException.ThrowIfNull(company);

        IEnumerable<string> words = Words(company).Where(word => !LegalSuffixes.Contains(word, StringComparer.Ordinal));

        return string.Join(' ', words);
    }

    /// <summary>Reduces a job title to the form two feeds are likely to agree on, without erasing what makes two openings different.</summary>
    public static string NormalizeTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        string stripped = TitleNoise().Replace(title, " ");

        return string.Join(' ', Words(stripped));
    }

    /// <summary>How alike two titles read, on the hundred-point scale deduplication is tuned against.</summary>
    public static int TitleSimilarity(string leftTitle, string rightTitle)
    {
        return Fuzz.WeightedRatio(NormalizeTitle(leftTitle), NormalizeTitle(rightTitle));
    }

    /// <summary>True when an incoming posting is the job the candidate already stands for.</summary>
    public static bool Matches(DuplicateCandidate candidate, string company, string title, DateTimeOffset seenAt)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (!string.Equals(NormalizeCompany(candidate.Company), NormalizeCompany(company), StringComparison.Ordinal))
        {
            return false;
        }

        if (seenAt - candidate.FirstSeenAt > TimeSpan.FromDays(WindowDays))
        {
            return false;
        }

        return TitleSimilarity(candidate.Title, title) >= MinimumSimilarity;
    }

    /// <summary>Returns the known job an incoming posting duplicates, the closest title first, or null when it is new.</summary>
    public static DuplicateCandidate? FindMatch(IEnumerable<DuplicateCandidate> candidates, string company, string title, DateTimeOffset seenAt)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        return candidates
            .Where(candidate => Matches(candidate, company, title, seenAt))
            .OrderByDescending(candidate => TitleSimilarity(candidate.Title, title))
            .ThenBy(candidate => candidate.FirstSeenAt)
            .FirstOrDefault();
    }

    private static IEnumerable<string> Words(string value)
    {
        return NonWord().Replace(value.ToLowerInvariant(), " ").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    [GeneratedRegex(@"[^a-z0-9#+]+")]
    private static partial Regex NonWord();

    [GeneratedRegex(@"\b(remote|hybrid|onsite|on[\s\-]site|[mfhwdx]\s*/\s*[mfhwdx](\s*/\s*[mfhwdx])?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TitleNoise();
}
