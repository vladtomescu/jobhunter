using System.Text;
using System.Text.RegularExpressions;

namespace JobHunter.Pipeline;

/// <summary>Turns a term typed in the settings into a regular expression that matches it literally as a whole token: a run of spaces matches any run of white space, and an edge of the term is guarded only where it is a word character, so terms such as .NET, C#, C++ and Node.js keep their punctuation.</summary>
internal static class LiteralTermPattern
{
    /// <summary>Whole-word matching as <c>\b</c> reads it: the term may not touch a letter, digit or underscore at an edge that is itself one.</summary>
    public static string WholeWord(string term)
    {
        return Build(term, @"\w", character => char.IsLetterOrDigit(character) || character == '_');
    }

    /// <summary>Letter-bounded matching: the term may not touch a letter at an edge that is itself a letter, so a version number may follow it (.NET8, Go1.22) while Google and going are not Go.</summary>
    public static string LetterBounded(string term)
    {
        return Build(term, @"\p{L}", char.IsLetter);
    }

    /// <summary>The trimmed, non-blank, distinct terms of a list, in the order given; terms that differ only in case count as one.</summary>
    public static string[] ReadTerms(IEnumerable<string> terms)
    {
        ArgumentNullException.ThrowIfNull(terms);

        return [.. terms.Select(term => term.Trim()).Where(term => term.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The terms of a text that holds them separated by one character, such as the comma lists and one-term-per-line lists the settings store.</summary>
    public static string[] ReadTerms(string text, char separator)
    {
        ArgumentNullException.ThrowIfNull(text);

        return ReadTerms(text.Split(separator));
    }

    private static string Build(string term, string guardedClass, Func<char, bool> needsGuard)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(term);

        string trimmed = term.Trim();
        StringBuilder pattern = new();

        if (needsGuard(trimmed[0]))
        {
            pattern.Append("(?<!").Append(guardedClass).Append(')');
        }

        pattern.AppendJoin(@"\s+", trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape));

        if (needsGuard(trimmed[^1]))
        {
            pattern.Append("(?!").Append(guardedClass).Append(')');
        }

        return pattern.ToString();
    }
}
