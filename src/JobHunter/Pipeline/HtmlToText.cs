using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace JobHunter.Pipeline;

/// <summary>Turns a description as a source delivers it, HTML from the feeds or markdown from the dataset, into plain text that keeps its paragraphs and bullets.</summary>
public static partial class HtmlToText
{
    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "source", "track", "wbr"
    };

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
        string text = DropHiddenElements(ScriptOrStyleBlock().Replace(Comment().Replace(html, string.Empty), " "));
        text = LineBreakTag().Replace(text, "\n");
        text = ListItemTag().Replace(text, "\n- ");
        text = BlockBoundaryTag().Replace(text, "\n\n");
        text = AnyTag().Replace(text, string.Empty);

        return text;
    }

    /// <summary>Drops every element a browser would not show, with everything inside it, so text hidden from a reader never reaches the scoring.</summary>
    /// <remarks>An element whose closing tag never comes loses only its opening tag, since the text after it cannot be told apart from the visible text that follows.</remarks>
    private static string DropHiddenElements(string html)
    {
        StringBuilder visible = new(html.Length);
        int position = 0;

        for (Match tag = Tag().Match(html); tag.Success; tag = Tag().Match(html, position))
        {
            int tagEnd = tag.Index + tag.Length;

            if (tag.Groups["closing"].Success || !IsHidden(tag.Groups["attributes"].Value))
            {
                visible.Append(html, position, tagEnd - position);
                position = tagEnd;
                continue;
            }

            visible.Append(html, position, tag.Index - position);
            position = EndOfElement(html, tag);
        }

        return visible.Append(html, position, html.Length - position).ToString();
    }

    /// <summary>True when the attributes of an opening tag keep it from being shown: the hidden attribute, aria-hidden set to true, or a style that sets display to none, visibility to hidden or the font size to zero.</summary>
    private static bool IsHidden(string attributes)
    {
        foreach (Match attribute in Attribute().Matches(attributes))
        {
            string name = attribute.Groups["name"].Value;
            string value = attribute.Groups["value"].Value;

            if (name.Equals("hidden", StringComparison.OrdinalIgnoreCase)
                || (name.Equals("aria-hidden", StringComparison.OrdinalIgnoreCase) && value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
                || (name.Equals("style", StringComparison.OrdinalIgnoreCase) && HidingStyle().IsMatch(value)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The position just past the closing tag that matches an opening tag, counting the tags of the same name nested inside it; for a void element or one never closed, the position just past the opening tag.</summary>
    /// <remarks>A slash at the end of an opening tag closes nothing, as in a browser: only a void element has no content.</remarks>
    private static int EndOfElement(string html, Match opening)
    {
        string name = opening.Groups["name"].Value;
        int openingEnd = opening.Index + opening.Length;

        if (VoidElements.Contains(name))
        {
            return openingEnd;
        }

        int depth = 1;

        for (Match tag = Tag().Match(html, openingEnd); tag.Success; tag = tag.NextMatch())
        {
            if (!tag.Groups["name"].Value.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (tag.Groups["closing"].Success)
            {
                depth--;

                if (depth == 0)
                {
                    return tag.Index + tag.Length;
                }
            }
            else
            {
                depth++;
            }
        }

        return openingEnd;
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

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex(@"<(?<closing>/)?(?<name>[a-z][a-z0-9-]*)(?<attributes>(?:\s[^>]*)?/?)>", RegexOptions.IgnoreCase)]
    private static partial Regex Tag();

    [GeneratedRegex(@"(?<name>[^\s""'=<>/]+)(?:\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)'|(?<value>[^\s""'=<>`]+)))?")]
    private static partial Regex Attribute();

    [GeneratedRegex(@"(?:^|;)\s*(?:display\s*:\s*none|visibility\s*:\s*hidden|font-size\s*:\s*0+(?:\.0+)?[a-z%]*)\s*(?:!\s*important\s*)?(?=;|$)", RegexOptions.IgnoreCase)]
    private static partial Regex HidingStyle();

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
