using System.Text.RegularExpressions;

namespace JobHunter.Applications;

/// <summary>One stretch of a header line, bold or not.</summary>
public sealed record CoverLetterRun(string Text, bool Bold);

/// <summary>One header line as it is printed: its runs, and whether it is the line that carries the candidate's name.</summary>
public sealed record CoverLetterHeaderLine(IReadOnlyList<CoverLetterRun> Runs, bool IsNameLine)
{
    /// <summary>The line as plain text, without the bold markers.</summary>
    public string Text => string.Concat(Runs.Select(run => run.Text));
}

/// <summary>Fills the Header lines of the cover-letter template from the settings: placeholders replaced, a segment whose placeholders are all empty dropped, a line with nothing left dropped, and bold markers turned into bold runs.</summary>
public static partial class CoverLetterHeader
{
    /// <summary>What separates the segments of a header line.</summary>
    public const string SegmentSeparator = " · ";

    /// <summary>The placeholder for the first and last name.</summary>
    public const string NamePlaceholder = "{name}";

    private const string BoldMarker = "**";

    /// <summary>The header lines as they are printed above the letter, filled from the settings as they are now.</summary>
    public static IReadOnlyList<CoverLetterHeaderLine> Render(IReadOnlyList<string> templateLines, Domain.Settings settings)
    {
        ArgumentNullException.ThrowIfNull(templateLines);
        ArgumentNullException.ThrowIfNull(settings);

        IReadOnlyDictionary<string, string> values = PlaceholderValues(settings);
        List<CoverLetterHeaderLine> lines = [];

        foreach (string templateLine in templateLines)
        {
            string kept = string.Join(SegmentSeparator, templateLine.Split(SegmentSeparator).Where(segment => !EveryPlaceholderEmpty(segment, values)));
            List<CoverLetterRun> runs = [.. Runs(kept).Select(run => run with { Text = Fill(run.Text, values) }).Where(run => run.Text.Length > 0)];

            if (runs.Any(run => !string.IsNullOrWhiteSpace(run.Text)))
            {
                lines.Add(new CoverLetterHeaderLine(runs, templateLine.Contains(NamePlaceholder, StringComparison.Ordinal)));
            }
        }

        return lines;
    }

    /// <summary>The name a letter is signed with: the first and last name from the settings, or empty when neither is set.</summary>
    public static string Name(Domain.Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return $"{settings.FirstName.Trim()} {settings.LastName.Trim()}".Trim();
    }

    private static Dictionary<string, string> PlaceholderValues(Domain.Settings settings)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [NamePlaceholder] = Name(settings),
            ["{email}"] = settings.Email.Trim(),
            ["{phone}"] = settings.Phone.Trim(),
            ["{location}"] = settings.Location.Trim(),
            ["{linkedin}"] = settings.LinkedInUrl.Trim()
        };
    }

    /// <summary>True when the segment names at least one known placeholder and every one it names is empty; a segment of plain text always stays.</summary>
    private static bool EveryPlaceholderEmpty(string segment, IReadOnlyDictionary<string, string> values)
    {
        string[] named = [.. Placeholder().Matches(segment).Select(match => match.Value).Where(values.ContainsKey)];

        return named.Length > 0 && named.All(placeholder => values[placeholder].Length == 0);
    }

    private static string Fill(string text, IReadOnlyDictionary<string, string> values)
    {
        foreach ((string placeholder, string value) in values)
        {
            text = text.Replace(placeholder, value, StringComparison.Ordinal);
        }

        return text;
    }

    /// <summary>Splits a line on its bold markers into alternating plain and bold runs; a marker without a partner stays as written.</summary>
    private static IEnumerable<CoverLetterRun> Runs(string line)
    {
        string[] parts = line.Split(BoldMarker);

        if (parts.Length % 2 == 0)
        {
            parts = [.. parts[..^2], parts[^2] + BoldMarker + parts[^1]];
        }

        for (int index = 0; index < parts.Length; index++)
        {
            yield return new CoverLetterRun(parts[index], Bold: index % 2 == 1);
        }
    }

    [GeneratedRegex(@"\{[a-z]+\}")]
    private static partial Regex Placeholder();
}
