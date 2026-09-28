namespace JobHunter.Llm;

/// <summary>Splits the cover-letter template into the Header section the app prints above the letter and the guidance the model is given without it.</summary>
/// <remarks>The Header section runs from its heading to the next second-level heading or the end of the file.</remarks>
public static class CoverLetterTemplate
{
    /// <summary>The heading of the section the app owns.</summary>
    public const string HeaderHeading = "## Header";

    private const string SectionHeadingPrefix = "## ";

    /// <summary>The non-blank lines of the Header section, trimmed and in order; empty when the template has no Header section.</summary>
    public static IReadOnlyList<string> HeaderLines(string template)
    {
        ArgumentNullException.ThrowIfNull(template);

        List<string> headerLines = [];
        bool inHeader = false;

        foreach (string line in SplitLines(template))
        {
            if (IsHeaderHeading(line))
            {
                inHeader = true;

                continue;
            }

            if (inHeader && line.StartsWith(SectionHeadingPrefix, StringComparison.Ordinal))
            {
                break;
            }

            if (inHeader && !string.IsNullOrWhiteSpace(line))
            {
                headerLines.Add(line.Trim());
            }
        }

        return headerLines;
    }

    /// <summary>The template with its Header section removed, heading included, and every other line left as written.</summary>
    public static string WithoutHeader(string template)
    {
        ArgumentNullException.ThrowIfNull(template);

        List<string> keptLines = [];
        bool inHeader = false;

        foreach (string line in SplitLines(template))
        {
            if (IsHeaderHeading(line))
            {
                inHeader = true;

                continue;
            }

            if (inHeader && line.StartsWith(SectionHeadingPrefix, StringComparison.Ordinal))
            {
                inHeader = false;
            }

            if (!inHeader)
            {
                keptLines.Add(line);
            }
        }

        return string.Join(Environment.NewLine, keptLines).Trim();
    }

    private static bool IsHeaderHeading(string line)
    {
        return string.Equals(line.TrimEnd(), HeaderHeading, StringComparison.Ordinal);
    }

    private static string[] SplitLines(string text)
    {
        return text.ReplaceLineEndings("\n").Split('\n');
    }
}
