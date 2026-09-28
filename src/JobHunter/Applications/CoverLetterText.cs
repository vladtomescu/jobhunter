using JobHunter.Domain;

namespace JobHunter.Applications;

/// <summary>A cover letter as it reads: the header printed from the template and the settings, the salutation, the paragraphs and the closing that were written, and the name under the closing.</summary>
public sealed record CoverLetterText(IReadOnlyList<CoverLetterHeaderLine> Header, string Salutation, IReadOnlyList<string> Paragraphs, string Closing, string Name)
{
    /// <summary>Puts a stored letter together with the header lines of the template and the settings as they are now.</summary>
    public static CoverLetterText Compose(ApplicationCoverLetter coverLetter, IReadOnlyList<string> headerTemplateLines, Domain.Settings settings)
    {
        ArgumentNullException.ThrowIfNull(coverLetter);

        return new CoverLetterText(
            CoverLetterHeader.Render(headerTemplateLines, settings),
            coverLetter.Salutation,
            coverLetter.Paragraphs,
            coverLetter.Closing,
            CoverLetterHeader.Name(settings));
    }

    /// <summary>The whole letter as plain text, ready to paste: the header lines together, then a blank line between blocks, the name right under the closing.</summary>
    public string ToPlainText()
    {
        List<string> blocks = [];

        if (Header.Count > 0)
        {
            blocks.Add(string.Join(Environment.NewLine, Header.Select(line => line.Text)));
        }

        blocks.Add(Salutation);
        blocks.AddRange(Paragraphs);
        blocks.Add(Name.Length == 0 ? Closing : $"{Closing}{Environment.NewLine}{Name}");

        return string.Join(Environment.NewLine + Environment.NewLine, blocks);
    }
}
