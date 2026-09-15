using JobHunter.Pipeline;

namespace JobHunter.Tests.Pipeline;

/// <summary>Proves that a description arrives as readable plain text whether the source sends HTML or markdown.</summary>
public sealed class HtmlToTextTests
{
    [Fact]
    public void Convert_ForHtmlParagraphs_KeepsTheParagraphBreak()
    {
        string text = HtmlToText.Convert("<p>We run .NET on Kubernetes.</p><p>You would own the platform.</p>");

        Assert.Equal("We run .NET on Kubernetes.\n\nYou would own the platform.", text);
    }

    [Fact]
    public void Convert_ForAnHtmlList_KeepsOneBulletPerItem()
    {
        string text = HtmlToText.Convert("<ul><li>Design services</li><li>Run them</li></ul>");

        Assert.Equal("- Design services\n\n- Run them", text);
    }

    [Fact]
    public void Convert_ForHtmlLineBreaks_TurnsThemIntoNewLines()
    {
        Assert.Equal("First line\nSecond line", HtmlToText.Convert("First line<br/>Second line"));
    }

    [Fact]
    public void Convert_ForHtmlEntities_DecodesThem()
    {
        Assert.Equal("Research & development \"at scale\"", HtmlToText.Convert("<p>Research &amp; development &quot;at scale&quot;</p>"));
    }

    [Fact]
    public void Convert_ForScriptAndStyleBlocks_DropsThemEntirely()
    {
        string text = HtmlToText.Convert("<style>.a{color:red}</style><p>Backend role</p><script>alert('x')</script>");

        Assert.Equal("Backend role", text);
    }

    [Fact]
    public void Convert_ForMarkdownHeadingsAndParagraphs_KeepsTheStructure()
    {
        string text = HtmlToText.Convert("## About the role\n\nWe run **.NET** on Kubernetes.\n");

        Assert.Equal("About the role\n\nWe run .NET on Kubernetes.", text);
    }

    [Fact]
    public void Convert_ForAMarkdownList_KeepsOneBulletPerItem()
    {
        string text = HtmlToText.Convert("Responsibilities:\n\n* Design services\n* Run them\n");

        Assert.Equal("Responsibilities:\n\n- Design services\n- Run them", text);
    }

    [Fact]
    public void Convert_ForAMarkdownLink_KeepsTheTextAndDropsTheTarget()
    {
        Assert.Equal("See our engineering blog.", HtmlToText.Convert("See our [engineering blog](https://example.com/blog)."));
    }

    [Fact]
    public void Convert_ForMarkdownQuotesAndRules_RemovesTheMarkers()
    {
        Assert.Equal("Quoted line\n\nPlain line", HtmlToText.Convert("> Quoted line\n\n---\n\nPlain line"));
    }

    [Fact]
    public void Convert_ForRunsOfBlankLines_LeavesOneBlankLine()
    {
        Assert.Equal("First\n\nSecond", HtmlToText.Convert("First\n\n\n\n\nSecond"));
    }

    [Fact]
    public void Convert_ForAnEmptyDescription_ReturnsAnEmptyString()
    {
        Assert.Equal(string.Empty, HtmlToText.Convert("   "));
    }

    [Theory]
    [InlineData("<p>Hello</p>", true)]
    [InlineData("Plain text with a < b comparison", false)]
    [InlineData("## Markdown heading", false)]
    public void LooksLikeHtml_ForADescription_RecognizesMarkup(string raw, bool expected)
    {
        Assert.Equal(expected, HtmlToText.LooksLikeHtml(raw));
    }
}
