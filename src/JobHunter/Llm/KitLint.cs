using System.Text.RegularExpressions;
using JobHunter.Llm.Contracts;
using JobHunter.Pipeline;

namespace JobHunter.Llm;

/// <summary>Reads a written kit line by line and reports every place where it breaks the rules the outward voice has to keep.</summary>
/// <remarks>A figure, a date or a notice period is allowed only on a line that carries the confirmation marker; the other rules admit no exception.</remarks>
public static partial class KitLint
{
    /// <summary>The marker that lets a line carry a figure, a date or a notice period.</summary>
    public const string ConfirmMarker = "[CONFIRM]";

    /// <summary>The one circulating currency code that also names a programming language, which a kit mentions far more often as the language.</summary>
    private const string LanguageNamedLikeACurrency = "PHP";

    private static readonly LintRule[] Rules =
    [
        new("currency amount without a confirmation marker", CurrencyAmount, ConfirmMarkerExempts: true, NamesACurrency),
        new("calendar date without a confirmation marker", CalendarDate, ConfirmMarkerExempts: true),
        new("notice period without a confirmation marker", NoticePeriod, ConfirmMarkerExempts: true),
        new("exclamation mark", ExclamationMark, ConfirmMarkerExempts: false),
        new("chain of em dashes", EmDashChain, ConfirmMarkerExempts: false),
        new("not just this but that construction", NotJustBut, ConfirmMarkerExempts: false),
        new("email address", EmailAddress, ConfirmMarkerExempts: false),
        new("phone number", PhoneNumber, ConfirmMarkerExempts: false),
        new("web link", WebLink, ConfirmMarkerExempts: false)
    ];

    /// <summary>Reports one issue per finding, each naming the rule, the offending fragment and the field it sits in; an empty list means the kit is clean.</summary>
    public static IReadOnlyList<string> Inspect(KitPayload kit)
    {
        ArgumentNullException.ThrowIfNull(kit);

        List<string> issues = [];

        for (int index = 0; index < kit.FitSummary.Count; index++)
        {
            Inspect(kit.FitSummary[index], $"fit_summary[{index}]", issues);
        }

        Inspect(kit.CoverNote, "cover_note", issues);

        for (int index = 0; index < kit.AtsAnswers.Count; index++)
        {
            Inspect(kit.AtsAnswers[index].Question, $"ats_answers[{index}].question", issues);
            Inspect(kit.AtsAnswers[index].Answer, $"ats_answers[{index}].answer", issues);
        }

        for (int index = 0; index < kit.CallQuestions.Count; index++)
        {
            Inspect(kit.CallQuestions[index], $"call_questions[{index}]", issues);
        }

        return issues;
    }

    private static void Inspect(string? text, string field, List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        foreach (string line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            bool confirmed = line.Contains(ConfirmMarker, StringComparison.Ordinal);

            foreach (LintRule rule in Rules)
            {
                if (rule.ConfirmMarkerExempts && confirmed)
                {
                    continue;
                }

                if (FirstFinding(rule, line) is Match finding)
                {
                    issues.Add($"{field}: {rule.Name}, \"{finding.Value.Trim()}\"");
                }
            }
        }
    }

    private static Match? FirstFinding(LintRule rule, string line)
    {
        foreach (Match match in rule.Pattern().Matches(line))
        {
            if (rule.Confirms is null || rule.Confirms(match))
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>A figure next to a currency symbol always names a currency; next to three capitals only when they are a circulating ISO 4217 code that does not also read as the PHP language.</summary>
    private static bool NamesACurrency(Match match)
    {
        Group code = match.Groups["code"];

        return !code.Success || (CurrencyCodes.IsCirculating(code.Value) && !string.Equals(code.Value, LanguageNamedLikeACurrency, StringComparison.Ordinal));
    }

    [GeneratedRegex(@"\p{Sc}\s?\d|\d[\d.,]*\s?[kKmM]?\s?\p{Sc}|\d[\d.,]*\s?[kKmM]?\s?(?<code>[A-Z]{3})\b|\b(?<code>[A-Z]{3})\s?\d")]
    private static partial Regex CurrencyAmount();

    [GeneratedRegex(@"\b\d{4}-\d{2}-\d{2}\b|\b\d{1,2}[./]\d{1,2}[./]\d{2,4}\b|\b(?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{1,2}(?:st|nd|rd|th)?\b|\b\d{1,2}(?:st|nd|rd|th)?\s+(?:January|February|March|April|May|June|July|August|September|October|November|December)\b")]
    private static partial Regex CalendarDate();

    [GeneratedRegex(@"\bnotice\b", RegexOptions.IgnoreCase)]
    private static partial Regex NoticePeriod();

    [GeneratedRegex(@"\S*!")]
    private static partial Regex ExclamationMark();

    [GeneratedRegex(@"—[^—]*—")]
    private static partial Regex EmDashChain();

    [GeneratedRegex(@"\bnot\s+just\b[^.?!]*\bbut\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotJustBut();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailAddress();

    [GeneratedRegex(@"\+\d[\d ().-]{7,}\d|\b\d{3}[ .-]\d{3}[ .-]\d{3,4}\b")]
    private static partial Regex PhoneNumber();

    [GeneratedRegex(@"(?:https?://|www\.)\S+", RegexOptions.IgnoreCase)]
    private static partial Regex WebLink();

    /// <summary>One rule: its name, its pattern, whether the confirmation marker exempts it, and an optional check a match has to pass to count as a finding.</summary>
    private sealed record LintRule(string Name, Func<Regex> Pattern, bool ConfirmMarkerExempts, Func<Match, bool>? Confirms = null);
}
