using System.Text.RegularExpressions;

namespace JobHunter.Pipeline;

/// <summary>Tells whether a posting names one of the candidate's stack keywords in its title, its tags or its text, which raises the stack-match flag.</summary>
/// <remarks>
/// A keyword matches literally and in any case, and may not touch a letter at an edge that is itself a letter: Go matches "Go" and "Go1.22" but not "Google" or "going", and C#, C++ and Node.js keep their punctuation.
/// A keyword that starts with a dot names a platform, and matches only as written: .NET matches ".NET" and "ASP.NET" but not the domain "example.net" or ".Net"; its spoken form (dot followed by the name, dotnet) matches as a whole word in any case.
/// </remarks>
public sealed class StackKeywordMatcher
{
    private const RegexOptions CaseSensitiveOptions = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    private const RegexOptions AnyCaseOptions = CaseSensitiveOptions | RegexOptions.IgnoreCase;

    private readonly (string Keyword, Regex Pattern)[] patterns;

    /// <summary>Compiles one pattern per keyword; blank and repeated keywords are ignored.</summary>
    public StackKeywordMatcher(IEnumerable<string> keywords)
    {
        patterns = [.. LiteralTermPattern.ReadTerms(keywords).Select(keyword => (keyword, Compile(keyword)))];
    }

    /// <summary>The keywords matched, trimmed and in the order given.</summary>
    public IReadOnlyList<string> Keywords => [.. patterns.Select(entry => entry.Keyword)];

    /// <summary>Reads the comma-separated keyword list the settings store.</summary>
    public static StackKeywordMatcher FromCommaList(string keywords)
    {
        ArgumentNullException.ThrowIfNull(keywords);

        return new StackKeywordMatcher(LiteralTermPattern.ReadTerms(keywords, ','));
    }

    /// <summary>True when any keyword appears in the title, a tag or the posting text.</summary>
    public bool Matches(string title, IEnumerable<string> tags, string descriptionText)
    {
        return FindKeyword(title, tags, descriptionText) is not null;
    }

    /// <summary>The first keyword, in the order given, that appears in the title, a tag or the posting text; null when none does.</summary>
    public string? FindKeyword(string title, IEnumerable<string> tags, string descriptionText)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(descriptionText);

        string[] tagList = [.. tags];

        foreach ((string keyword, Regex pattern) in patterns)
        {
            if (pattern.IsMatch(title) || tagList.Any(pattern.IsMatch) || pattern.IsMatch(descriptionText))
            {
                return keyword;
            }
        }

        return null;
    }

    private static Regex Compile(string keyword)
    {
        if (keyword.Length > 1 && keyword[0] == '.')
        {
            string spokenForm = LiteralTermPattern.LetterBounded("dot" + keyword[1..]);

            return new Regex($"{LiteralTermPattern.LetterBounded(keyword)}|(?i:{spokenForm})", CaseSensitiveOptions);
        }

        return new Regex(LiteralTermPattern.LetterBounded(keyword), AnyCaseOptions);
    }
}
