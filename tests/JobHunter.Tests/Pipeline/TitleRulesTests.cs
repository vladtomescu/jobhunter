using JobHunter.Pipeline;

namespace JobHunter.Tests.Pipeline;

/// <summary>Proves that the title rules, built from the default title terms, keep engineering work and turn away the roles outside the search, matching each term as a whole word.</summary>
public sealed class TitleRulesTests
{
    private readonly ITitleRules rules = CandidateProfiles.NetherlandsJava.TitleRules;

    [Theory]
    [InlineData("Staff Software Engineer")]
    [InlineData("Backend Engineer (Python)")]
    [InlineData("Senior .NET Developer")]
    [InlineData("Principal Platform Engineer")]
    [InlineData("Site Reliability Engineer")]
    [InlineData("Distributed Systems Engineer")]
    [InlineData("Infrastructure Engineer, Developer Tools")]
    [InlineData("Inginer software backend")]
    [InlineData("AI Tooling Engineer")]
    public void Evaluate_ForEngineeringTitles_Includes(string title)
    {
        Assert.Equal(TitleVerdictKind.Included, rules.Evaluate(title).Kind);
    }

    [Theory]
    [InlineData("Senior Frontend Engineer", "frontend")]
    [InlineData("Front-End Developer", "front-end")]
    [InlineData("Senior Android Engineer", "android")]
    [InlineData("QA Automation Engineer", "qa")]
    [InlineData("Sales Engineer", "sales")]
    [InlineData("Marketing Engineer", "marketing")]
    [InlineData("Product Designer", "designer")]
    [InlineData("Senior Data Scientist", "data scientist")]
    [InlineData("Machine Learning Research Engineer", "machine learning research")]
    [InlineData("Junior Backend Developer", "junior")]
    [InlineData("Backend Engineering Intern", "intern")]
    [InlineData("Engineering Manager", "manager")]
    [InlineData("Director of Engineering", "director")]
    public void Evaluate_ForRolesOutsideTheSearch_ExcludesWithTheReason(string title, string reason)
    {
        TitleVerdict verdict = rules.Evaluate(title);

        Assert.Equal(TitleVerdictKind.ExcludedByRule, verdict.Kind);
        Assert.Equal(reason, verdict.Reason);
    }

    [Theory]
    [InlineData("Customer Success Specialist")]
    [InlineData("Technical Writer")]
    [InlineData("Scrum Master")]
    public void Evaluate_ForTitlesWithoutAnEngineeringTerm_ReportsTheMiss(string title)
    {
        Assert.Equal(TitleVerdictKind.NoIncludeTerm, rules.Evaluate(title).Kind);
    }

    [Fact]
    public void Evaluate_ForATitleThatIsBothExcludedAndIncluded_LetsTheExclusionWin()
    {
        Assert.Equal(TitleVerdictKind.ExcludedByRule, rules.Evaluate("Senior Frontend Engineer").Kind);
    }

    [Theory]
    [InlineData("Internal Tools Engineer")]
    [InlineData("Salesforce Platform Engineer")]
    public void Evaluate_ForATitleThatContainsAnExcludeTermInsideAWord_Includes(string title)
    {
        Assert.Equal(TitleVerdictKind.Included, rules.Evaluate(title).Kind);
    }

    [Theory]
    [InlineData("Senior C# Developer", "c#", true)]
    [InlineData("ASP.NET Specialist", ".net", true)]
    [InlineData("Node.js Specialist", "node.js", true)]
    [InlineData("Nodejs Specialist", "node.js", false)]
    [InlineData("Go Specialist", "go", true)]
    [InlineData("Google Specialist", "go", false)]
    [InlineData("Site  Reliability Specialist", "site reliability", true)]
    public void Evaluate_ForAnIncludeTermWithPunctuationOrSpaces_MatchesTheLiteralToken(string title, string term, bool included)
    {
        TitleRules single = new([term], []);

        Assert.Equal(included ? TitleVerdictKind.Included : TitleVerdictKind.NoIncludeTerm, single.Evaluate(title).Kind);
    }

    [Fact]
    public void Evaluate_WithNoIncludeTerms_IncludesEveryTitleThatTripsNoExclusion()
    {
        TitleRules rulesWithoutIncludes = TitleRules.FromLines(string.Empty, "manager");

        Assert.Equal(TitleVerdictKind.Included, rulesWithoutIncludes.Evaluate("Executive Chef").Kind);
        Assert.Equal(TitleVerdictKind.ExcludedByRule, rulesWithoutIncludes.Evaluate("Kitchen Manager").Kind);
    }

    [Fact]
    public void FromLines_ForTermsWithBlankLinesAndCarriageReturns_ReadsOneTermPerLine()
    {
        TitleRules fromLines = TitleRules.FromLines("engineer\r\n\r\n developer \n", "intern\r\n");

        Assert.Equal(TitleVerdictKind.Included, fromLines.Evaluate("Software Developer").Kind);
        Assert.Equal("intern", fromLines.Evaluate("Engineer Intern").Reason);
    }

    [Theory]
    [InlineData("Staff Software Engineer", true)]
    [InlineData("Senior Backend Engineer", true)]
    [InlineData("Principal Engineer", true)]
    [InlineData("Lead Platform Engineer", true)]
    [InlineData("Backend Engineer", false)]
    [InlineData("Software Developer", false)]
    public void IsSeniorLevelled_ForATitle_TellsWhetherItIsLevelledSeniorOrAbove(string title, bool expected)
    {
        Assert.Equal(expected, TitleRules.IsSeniorLevelled(title));
    }
}
