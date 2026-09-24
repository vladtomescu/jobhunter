using JobHunter.Pipeline;

namespace JobHunter.Tests.Domain;

/// <summary>Proves that the default title terms say what the built-in title rules already do, term by term, so moving the rules onto the settings changes no verdict.</summary>
public sealed class SettingsTests
{
    private readonly TitleRules titleRules = new();

    public static TheoryData<string> DefaultIncludeTerms => [.. JobHunter.Domain.Settings.DefaultTitleIncludeTerms.Split('\n')];

    public static TheoryData<string> DefaultExcludeTerms => [.. JobHunter.Domain.Settings.DefaultTitleExcludeTerms.Split('\n')];

    [Theory]
    [MemberData(nameof(DefaultIncludeTerms))]
    public void DefaultTitleIncludeTerms_EachTermAsATitle_IsIncludedByTheTitleRules(string term)
    {
        Assert.Equal(TitleVerdictKind.Included, titleRules.Evaluate(term).Kind);
    }

    [Theory]
    [MemberData(nameof(DefaultExcludeTerms))]
    public void DefaultTitleExcludeTerms_EachTermNextToAnIncludeTerm_IsExcludedByTheTitleRules(string term)
    {
        Assert.Equal(TitleVerdictKind.ExcludedByRule, titleRules.Evaluate($"{term} software engineer").Kind);
    }
}
