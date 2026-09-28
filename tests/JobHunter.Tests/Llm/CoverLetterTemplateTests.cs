using JobHunter.Llm;

namespace JobHunter.Tests.Llm;

/// <summary>Proves that the cover-letter template splits into the Header section the app prints and the guidance the model reads without it.</summary>
public sealed class CoverLetterTemplateTests
{
    private const string Template = """
        # Cover letter template

        Guidance before the header.

        ## Header

        **{name}**

        {location} · {email}
        ### Not a section break
        ## Length and shape

        - One page.
        """;

    [Fact]
    public void HeaderLines_OfATemplateWithAHeaderSection_ReadTheNonBlankLinesUpToTheNextSection()
    {
        IReadOnlyList<string> lines = CoverLetterTemplate.HeaderLines(Template);

        Assert.Equal<string>(["**{name}**", "{location} · {email}", "### Not a section break"], lines);
    }

    [Fact]
    public void HeaderLines_OfATemplateWithoutAHeaderSection_AreEmpty()
    {
        Assert.Empty(CoverLetterTemplate.HeaderLines("# Cover letter template\n\n## Tone\n\nCalm."));
    }

    [Fact]
    public void HeaderLines_OfAHeaderSectionAtTheEndOfTheFile_ReadToTheEnd()
    {
        IReadOnlyList<string> lines = CoverLetterTemplate.HeaderLines("# Template\r\n\r\n## Header\r\n{name}\r\n{phone}\r\n");

        Assert.Equal<string>(["{name}", "{phone}"], lines);
    }

    [Fact]
    public void WithoutHeader_OfATemplateWithAHeaderSection_DropsTheHeadingAndItsLinesAndKeepsEverythingElse()
    {
        string guidance = CoverLetterTemplate.WithoutHeader(Template);

        Assert.DoesNotContain(CoverLetterTemplate.HeaderHeading, guidance, StringComparison.Ordinal);
        Assert.DoesNotContain("{name}", guidance, StringComparison.Ordinal);
        Assert.DoesNotContain("{email}", guidance, StringComparison.Ordinal);
        Assert.StartsWith("# Cover letter template", guidance, StringComparison.Ordinal);
        Assert.Contains("Guidance before the header.", guidance, StringComparison.Ordinal);
        Assert.Contains("## Length and shape", guidance, StringComparison.Ordinal);
        Assert.EndsWith("- One page.", guidance, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutHeader_OfATemplateWithoutAHeaderSection_LeavesItAsWritten()
    {
        const string template = "# Cover letter template\n\n## Tone\n\nCalm.";

        Assert.Equal(template.ReplaceLineEndings(), CoverLetterTemplate.WithoutHeader(template));
    }
}
