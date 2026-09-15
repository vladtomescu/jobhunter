using JobHunter.Pipeline;

namespace JobHunter.Tests.Pipeline;

/// <summary>Proves that the title rules keep engineering work and turn away the roles outside the search.</summary>
public sealed class TitleRulesTests
{
    private readonly TitleRules rules = new();

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
    [InlineData("Front-End Developer", "frontend")]
    [InlineData("Senior Android Engineer", "mobile")]
    [InlineData("QA Automation Engineer", "quality assurance")]
    [InlineData("Sales Engineer", "sales")]
    [InlineData("Marketing Engineer", "marketing")]
    [InlineData("Product Designer", "design")]
    [InlineData("Senior Data Scientist", "data science")]
    [InlineData("Machine Learning Research Engineer", "machine learning research")]
    [InlineData("Junior Backend Developer", "early career")]
    [InlineData("Backend Engineering Intern", "early career")]
    [InlineData("Engineering Manager", "management")]
    [InlineData("Director of Engineering", "management")]
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
