using JobHunter.Pipeline;

namespace JobHunter.Tests.Pipeline;

/// <summary>Proves that stack keywords match as literal tokens in the title, the tags and the posting text, with .NET keeping its case-sensitive precision.</summary>
public sealed class StackKeywordMatcherTests
{
    private readonly StackKeywordMatcher dotNet = StackKeywordMatcher.FromCommaList(".NET, C#");

    [Theory]
    [InlineData(".NET")]
    [InlineData("C#")]
    [InlineData("ASP.NET")]
    [InlineData("dotnet")]
    public void Matches_ForADotNetTermInTheTitle_ReturnsTrue(string term)
    {
        Assert.True(dotNet.Matches($"Senior {term} Engineer", [], "Plain text."));
    }

    [Theory]
    [InlineData(".NET")]
    [InlineData("C#")]
    [InlineData("ASP.NET")]
    [InlineData("dotnet")]
    public void Matches_ForADotNetTermInTheTags_ReturnsTrue(string term)
    {
        Assert.True(dotNet.Matches("Backend Engineer", ["backend", term], "Plain text."));
    }

    [Theory]
    [InlineData(".NET")]
    [InlineData("C#")]
    [InlineData("ASP.NET")]
    [InlineData("dotnet")]
    public void Matches_ForADotNetTermInThePostingText_ReturnsTrue(string term)
    {
        Assert.True(dotNet.Matches("Backend Engineer", [], $"You will build services in {term} on Kubernetes."));
    }

    [Theory]
    [InlineData("C#/.NET developer")]
    [InlineData("Experience with c# and SQL")]
    [InlineData("We ship .NET 8 services")]
    [InlineData("We ship .NET8 services")]
    [InlineData("Our DotNet platform")]
    public void Matches_ForAUsualDotNetSpelling_ReturnsTrue(string text)
    {
        Assert.True(dotNet.Matches("Backend Engineer", [], text));
    }

    [Theory]
    [InlineData("Java")]
    [InlineData("Node")]
    [InlineData("We build network software.")]
    [InlineData("Apply at https://careers.example.net/jobs today.")]
    [InlineData("Read more on lemon.net")]
    [InlineData("Our .Net platform")]
    [InlineData("Objective-C and Swift")]
    [InlineData("Languages: c, Rust")]
    public void Matches_ForTextThatNamesNoDotNetTerm_ReturnsFalse(string text)
    {
        Assert.False(dotNet.Matches(text, [text], text));
    }

    [Theory]
    [InlineData("Node.js", "Backend in Node.js and Postgres", true)]
    [InlineData("Node.js", "Backend in node.js and Postgres", true)]
    [InlineData("Node.js", "Backend in Node and Postgres", false)]
    [InlineData("C++", "Low-latency C++17 trading systems", true)]
    [InlineData("C++", "Written in C and Rust", false)]
    [InlineData("C++", "We use C# on the server", false)]
    [InlineData("Go", "Services in Go and Postgres", true)]
    [InlineData("Go", "Services in go, Kafka and Postgres", true)]
    [InlineData("Go", "Upgraded to Go1.22 last month", true)]
    [InlineData("Go", "We work with Google Cloud", false)]
    [InlineData("Go", "An ongoing project that keeps going", false)]
    [InlineData("Java", "JVM work in Java 21", true)]
    [InlineData("Java", "Frontend in JavaScript", false)]
    public void Matches_ForAKeywordWithPunctuationOrAShortName_MatchesOnlyTheWholeToken(string keyword, string text, bool expected)
    {
        Assert.Equal(expected, new StackKeywordMatcher([keyword]).Matches("Backend Engineer", [], text));
    }

    [Fact]
    public void FindKeyword_ForAPostingThatNamesTwoKeywords_ReturnsTheFirstConfigured()
    {
        Assert.Equal(".NET", dotNet.FindKeyword("Backend Engineer", ["c#"], "We run .NET on Azure."));
    }

    [Fact]
    public void FindKeyword_ForAnEmptyKeywordList_ReturnsNull()
    {
        StackKeywordMatcher none = StackKeywordMatcher.FromCommaList(" , ");

        Assert.Empty(none.Keywords);
        Assert.Null(none.FindKeyword("Senior .NET Engineer", [".NET"], "C# and .NET"));
    }

    [Fact]
    public void FromCommaList_ForSpacedAndRepeatedKeywords_KeepsEachOnceInOrder()
    {
        Assert.Equal<string>([".NET", "C#", "go"], StackKeywordMatcher.FromCommaList(" .NET ,C#, go ,Go").Keywords);
    }
}
