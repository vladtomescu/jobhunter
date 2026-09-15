using System.Net;
using System.Text.RegularExpressions;

namespace JobHunter.Pipeline;

/// <summary>Turns a description as a source delivers it, HTML from the feeds or markdown from the dataset, into plain text that keeps its paragraphs and bullets.</summary>
public static partial class HtmlToText
{
    /// <summary>Converts one raw description to plain text.</summary>
    public static string Convert(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        string text = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        text = LooksLikeHtml(text) ? StripMarkup(text) : StripMarkdown(text);

        return Tidy(WebUtility.HtmlDecode(text));
    }

    /// <summary>True when the text carries HTML markup rather than markdown or plain prose.</summary>
    public static bool LooksLikeHtml(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return HtmlTag().IsMatch(text);
    }

    private static string StripMarkup(string html)
    {
        string text = ScriptOrStyleBlock().Replace(html, " ");
        text = LineBreakTag().Replace(text, "\n");
        text = ListItemTag().Replace(text, "\n- ");
        text = BlockBoundaryTag().Replace(text, "\n\n");
        text = AnyTag().Replace(text, string.Empty);

        return text;
    }

    private static string StripMarkdown(string markdown)
    {
        string text = FenceLine().Replace(markdown, string.Empty);
        text = MarkdownImage().Replace(text, string.Empty);
        text = MarkdownLink().Replace(text, "$1");
        text = HeadingMarker().Replace(text, string.Empty);
        text = QuoteMarker().Replace(text, string.Empty);
        text = HorizontalRule().Replace(text, "\n");
        text = BulletMarker().Replace(text, "- ");
        text = EmphasisMarker().Replace(text, string.Empty);

        return text;
    }

    private static string Tidy(string text)
    {
        string collapsed = HorizontalWhitespace().Replace(text, " ");
        IEnumerable<string> lines = collapsed.Split('\n').Select(line => line.Trim());

        return BlankLineRun().Replace(string.Join('\n', lines), "\n\n").Trim();
    }

    [GeneratedRegex(@"<(?:/?[a-z][a-z0-9]*\b[^>]*|!--)>", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptOrStyleBlock();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakTag();

    [GeneratedRegex(@"<li\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ListItemTag();

    [GeneratedRegex(@"</?(p|div|section|article|header|footer|ul|ol|li|table|tr|h[1-6]|blockquote|pre)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockBoundaryTag();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"^[ \t]*(```|~~~).*$", RegexOptions.Multiline)]
    private static partial Regex FenceLine();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex MarkdownImage();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"^[ \t]*#{1,6}[ \t]+", RegexOptions.Multiline)]
    private static partial Regex HeadingMarker();

    [GeneratedRegex(@"^[ \t]*>[ \t]?", RegexOptions.Multiline)]
    private static partial Regex QuoteMarker();

    [GeneratedRegex(@"^[ \t]*([-*_])(?:[ \t]*\1){2,}[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex HorizontalRule();

    [GeneratedRegex(@"^[ \t]*[-*+][ \t]+", RegexOptions.Multiline)]
    private static partial Regex BulletMarker();

    [GeneratedRegex(@"(\*\*|__|\*|_|`)")]
    private static partial Regex EmphasisMarker();

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex HorizontalWhitespace();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLineRun();
}
