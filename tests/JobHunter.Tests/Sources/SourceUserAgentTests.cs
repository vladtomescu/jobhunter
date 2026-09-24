using JobHunter.Sources;

namespace JobHunter.Tests.Sources;

/// <summary>Proves the single User-Agent builder every source and the pipeline registration shares.</summary>
public sealed class SourceUserAgentTests
{
    [Fact]
    public void Build_WithAnEmail_ReturnsTheProductStringWithAMailtoContact()
    {
        string userAgent = SourceUserAgent.Build("test@example.com");

        Assert.Equal("JobHunter/1.0 (+mailto:test@example.com)", userAgent);
    }

    [Fact]
    public void Build_WithAnEmailCarryingSurroundingWhitespace_TrimsItBeforeBuildingTheContact()
    {
        string userAgent = SourceUserAgent.Build("  test@example.com  ");

        Assert.Equal("JobHunter/1.0 (+mailto:test@example.com)", userAgent);
    }

    [Fact]
    public void Build_WithANullEmail_ReturnsTheBareProductString()
    {
        string userAgent = SourceUserAgent.Build(null!);

        Assert.Equal("JobHunter/1.0", userAgent);
    }

    [Fact]
    public void Build_WithAnEmptyEmail_ReturnsTheBareProductString()
    {
        string userAgent = SourceUserAgent.Build(string.Empty);

        Assert.Equal("JobHunter/1.0", userAgent);
    }

    [Fact]
    public void Build_WithAWhitespaceOnlyEmail_ReturnsTheBareProductString()
    {
        string userAgent = SourceUserAgent.Build("   ");

        Assert.Equal("JobHunter/1.0", userAgent);
    }
}
