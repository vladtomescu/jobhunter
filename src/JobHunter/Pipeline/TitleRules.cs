using System.Text.RegularExpressions;

namespace JobHunter.Pipeline;

/// <summary>The deterministic title rules from the candidate's settings: a title is kept when it carries an include term and trips no exclude term, each term matched literally as a whole word in any case.</summary>
/// <remarks>An empty include list sets no requirement, so every title that trips no exclude term is kept.</remarks>
public sealed partial class TitleRules : ITitleRules
{
    private const RegexOptions TermOptions = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private readonly string[] excludeTerms;

    private readonly Regex? excludePattern;

    private readonly Regex? includePattern;

    /// <summary>Compiles the include and exclude terms once; blank and repeated terms are ignored.</summary>
    public TitleRules(IEnumerable<string> includeTerms, IEnumerable<string> excludeTerms)
    {
        string[] include = LiteralTermPattern.ReadTerms(includeTerms);
        this.excludeTerms = LiteralTermPattern.ReadTerms(excludeTerms);

        includePattern = include.Length == 0 ? null : new Regex(string.Join('|', include.Select(LiteralTermPattern.WholeWord)), TermOptions);
        excludePattern = this.excludeTerms.Length == 0 ? null : new Regex(string.Join('|', this.excludeTerms.Select(term => $"({LiteralTermPattern.WholeWord(term)})")), TermOptions);
    }

    /// <summary>Reads the one-term-per-line lists the settings store.</summary>
    public static TitleRules FromLines(string includeTerms, string excludeTerms)
    {
        return new TitleRules(LiteralTermPattern.ReadTerms(includeTerms, '\n'), LiteralTermPattern.ReadTerms(excludeTerms, '\n'));
    }

    /// <inheritdoc />
    /// <remarks>The reason of an exclusion is the exclude term that matched first in the title.</remarks>
    public TitleVerdict Evaluate(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        if (excludePattern?.Match(title) is { Success: true } exclusion)
        {
            return TitleVerdict.ExcludedByRule(MatchedExcludeTerm(exclusion));
        }

        return includePattern is null || includePattern.IsMatch(title) ? TitleVerdict.Included : TitleVerdict.NoIncludeTerm;
    }

    /// <summary>True when the title is levelled senior or above, which raises the H1 flag.</summary>
    public static bool IsSeniorLevelled(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        return SeniorLevel().IsMatch(title);
    }

    private string MatchedExcludeTerm(Match exclusion)
    {
        for (int index = 0; index < excludeTerms.Length; index++)
        {
            if (exclusion.Groups[index + 1].Success)
            {
                return excludeTerms[index];
            }
        }

        return exclusion.Value;
    }

    [GeneratedRegex(@"\b(senior|sr|staff|principal|lead|distinguished|architect)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SeniorLevel();
}
