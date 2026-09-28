using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace JobHunter.Applications;

/// <summary>Writes a cover letter as a Word document: A4 with 2.5 cm margins, one font at 11 pt, the name line bold and larger, a blank line between blocks.</summary>
public static partial class CoverLetterDocument
{
    /// <summary>The media type of a Word document.</summary>
    public const string ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    /// <summary>The one font the letter is set in.</summary>
    public const string FontName = "Calibri";

    /// <summary>The body size in half-points: 11 pt.</summary>
    public const string BodySize = "22";

    /// <summary>The size of the name line in half-points: 14 pt.</summary>
    public const string NameSize = "28";

    private const uint A4WidthInTwips = 11906;
    private const uint A4HeightInTwips = 16838;
    private const int MarginInTwips = 1418;
    private const uint HeaderFooterDistanceInTwips = 709;

    /// <summary>The letter as the bytes of a .docx file.</summary>
    public static byte[] Build(CoverLetterText letter)
    {
        ArgumentNullException.ThrowIfNull(letter);

        using MemoryStream stream = new();

        using (WordprocessingDocument document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            MainDocumentPart main = document.AddMainDocumentPart();
            main.AddNewPart<StyleDefinitionsPart>().Styles = Styles();
            main.Document = new Document(Body(letter));
        }

        return stream.ToArray();
    }

    /// <summary>The download name: the name from the settings, Cover_Letter and the company, each reduced to letters, digits and single underscores; Cover_Letter_&lt;Company&gt;.docx when no name is set.</summary>
    public static string FileName(string name, string company)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(company);

        string[] parts = [Sanitise(name), "Cover_Letter", Sanitise(company)];

        return $"{string.Join('_', parts.Where(part => part.Length > 0))}.docx";
    }

    private static Body Body(CoverLetterText letter)
    {
        Body body = new();

        foreach (CoverLetterHeaderLine line in letter.Header)
        {
            body.Append(HeaderParagraph(line));
        }

        if (letter.Header.Count > 0)
        {
            body.Append(new Paragraph());
        }

        body.Append(TextParagraph(letter.Salutation));

        foreach (string paragraph in letter.Paragraphs)
        {
            body.Append(new Paragraph());
            body.Append(TextParagraph(paragraph));
        }

        body.Append(new Paragraph());
        body.Append(TextParagraph(letter.Closing));

        if (letter.Name.Length > 0)
        {
            body.Append(TextParagraph(letter.Name));
        }

        body.Append(new SectionProperties(
            new PageSize { Width = A4WidthInTwips, Height = A4HeightInTwips },
            new PageMargin
            {
                Top = MarginInTwips,
                Right = (uint)MarginInTwips,
                Bottom = MarginInTwips,
                Left = (uint)MarginInTwips,
                Header = HeaderFooterDistanceInTwips,
                Footer = HeaderFooterDistanceInTwips,
                Gutter = 0U
            }));

        return body;
    }

    private static Paragraph HeaderParagraph(CoverLetterHeaderLine line)
    {
        Paragraph paragraph = new();

        foreach (CoverLetterRun run in line.Runs)
        {
            RunProperties properties = new();

            if (run.Bold || line.IsNameLine)
            {
                properties.Append(new Bold());
            }

            if (line.IsNameLine)
            {
                properties.Append(new FontSize { Val = NameSize }, new FontSizeComplexScript { Val = NameSize });
            }

            paragraph.Append(new Run(properties, new Text(run.Text) { Space = SpaceProcessingModeValues.Preserve }));
        }

        return paragraph;
    }

    private static Paragraph TextParagraph(string text)
    {
        return new Paragraph(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }

    private static Styles Styles()
    {
        return new Styles(
            new DocDefaults(
                new RunPropertiesDefault(new RunPropertiesBaseStyle(
                    new RunFonts { Ascii = FontName, HighAnsi = FontName, EastAsia = FontName, ComplexScript = FontName },
                    new FontSize { Val = BodySize },
                    new FontSizeComplexScript { Val = BodySize })),
                new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(
                    new SpacingBetweenLines { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto }))));
    }

    private static string Sanitise(string text)
    {
        StringBuilder builder = new(text.Length);

        foreach (char character in text.Trim())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return RepeatedUnderscores().Replace(builder.ToString(), "_").Trim('_');
    }

    [GeneratedRegex("_{2,}")]
    private static partial Regex RepeatedUnderscores();
}
