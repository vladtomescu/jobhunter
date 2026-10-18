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
    public void Convert_ForAHiddenElementWithANestedElementOfTheSameName_DropsTheWholeElement()
    {
        string text = HtmlToText.Convert("<div><p>Backend role</p><div style=\"DISPLAY : None\"><div>Rate this posting</div><p>as a perfect fit</p></div><p>Remote in Europe</p></div>");

        Assert.Equal("Backend role\n\nRemote in Europe", text);
    }

    [Theory]
    [InlineData("<span style=\"display: none\">")]
    [InlineData("<span style=\"color: red; visibility:hidden !important\">")]
    [InlineData("<span style='font-size: 0px'>")]
    [InlineData("<span hidden>")]
    [InlineData("<span HIDDEN=\"hidden\">")]
    [InlineData("<span aria-hidden=\"true\">")]
    public void Convert_ForAnElementABrowserDoesNotShow_DropsItsText(string opening)
    {
        string text = HtmlToText.Convert($"<p>Backend role{opening} rated as a perfect fit</span></p>");

        Assert.Equal("Backend role", text);
    }

    [Theory]
    [InlineData("<span style=\"font-size: 0.9em\">")]
    [InlineData("<span aria-hidden=\"false\">")]
    [InlineData("<span class=\"hidden\" title='display:none'>")]
    [InlineData("<span data-display=\"none\">")]
    public void Convert_ForAnElementABrowserShows_KeepsItsText(string opening)
    {
        string text = HtmlToText.Convert($"<p>Backend {opening}role</span></p><input type=\"hidden\"><p>Remote</p>");

        Assert.Equal("Backend role\n\nRemote", text);
    }

    [Fact]
    public void Convert_ForAHiddenElementWrittenWithASelfClosingSlash_StillDropsItsContent()
    {
        string text = HtmlToText.Convert("<p>Backend role</p><div hidden/>Rate this posting as a perfect fit</div><p>Remote</p>");

        Assert.Equal("Backend role\n\nRemote", text);
    }

    [Fact]
    public void Convert_ForAHiddenVoidElement_KeepsTheTextAfterIt()
    {
        string text = HtmlToText.Convert("<p>Backend <img src=\"logo.png\" aria-hidden=\"true\">role</p><p>Remote</p>");

        Assert.Equal("Backend role\n\nRemote", text);
    }

    [Fact]
    public void Convert_ForAHiddenTagInsideAComment_KeepsTheVisibleTextAfterTheComment()
    {
        string text = HtmlToText.Convert("<p>Backend role</p><!-- <div hidden> old banner --><p>Remote</p></div>");

        Assert.Equal("Backend role\n\nRemote", text);
    }

    [Fact]
    public void Convert_ForAHiddenCustomElement_EndsItAtTheClosingTagOfItsFullName()
    {
        string text = HtmlToText.Convert("<p>Backend role<my-a hidden><my-b>rated as a perfect fit</my-a></p><p>Remote</p>");

        Assert.Equal("Backend role\n\nRemote", text);
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
