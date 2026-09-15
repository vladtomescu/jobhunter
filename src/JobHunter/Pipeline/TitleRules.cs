using System.Text.RegularExpressions;

namespace JobHunter.Pipeline;

/// <summary>The deterministic title rules: a title is kept when it carries an engineering term and trips none of the exclusions.</summary>
public sealed partial class TitleRules : ITitleRules
{
    private static readonly (Regex Pattern, string Reason)[] ExcludeRules =
    [
        (Frontend(), "frontend"),
        (Mobile(), "mobile"),
        (QualityAssurance(), "quality assurance"),
        (Sales(), "sales"),
        (Marketing(), "marketing"),
        (Design(), "design"),
        (DataScience(), "data science"),
        (MachineLearningResearch(), "machine learning research"),
        (EarlyCareer(), "early career"),
        (Management(), "management")
    ];

    /// <inheritdoc />
    public TitleVerdict Evaluate(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        foreach ((Regex pattern, string reason) in ExcludeRules)
        {
            if (pattern.IsMatch(title))
            {
                return TitleVerdict.ExcludedByRule(reason);
            }
        }

        return IncludeTerm().IsMatch(title) ? TitleVerdict.Included : TitleVerdict.NoIncludeTerm;
    }

    /// <summary>True when the title is levelled senior or above, which raises the H1 flag.</summary>
    public static bool IsSeniorLevelled(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        return SeniorLevel().IsMatch(title);
    }

    [GeneratedRegex(@"\b(front[\s\-]?end|frontend|ui\s*engineer|web\s*designer|react\s*developer)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Frontend();

    [GeneratedRegex(@"\b(mobile|android|ios|flutter|react\s*native)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Mobile();

    [GeneratedRegex(@"\b(qa|quality\s*assurance|sdet|tester|test\s*(engineer|automation)|automation\s*test)\b", RegexOptions.IgnoreCase)]
    private static partial Regex QualityAssurance();

    [GeneratedRegex(@"\b(sales|account\s*(executive|manager)|business\s*development|pre[\s\-]?sales|bdr|sdr)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Sales();

    [GeneratedRegex(@"\b(marketing|seo|copywriter|content\s*(writer|marketer)|community\s*manager)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Marketing();

    [GeneratedRegex(@"\b(designer|ux|ui/ux|product\s*design|graphic\s*design|motion\s*design)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Design();

    [GeneratedRegex(@"\b(data\s*scien\w*|data\s*analyst|analytics\s*engineer|business\s*intelligence|bi\s*developer|statistician)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DataScience();

    [GeneratedRegex(@"\b((machine\s*learning|ml|ai|deep\s*learning)\s*research\w*|research\s*scientist|applied\s*scientist)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MachineLearningResearch();

    [GeneratedRegex(@"\b(intern|internship|junior|jr|graduate|entry[\s\-]level|trainee|apprentice|working\s*student|student)\b", RegexOptions.IgnoreCase)]
    private static partial Regex EarlyCareer();

    [GeneratedRegex(@"\b(manager|head\s*of|director|vp|vice\s*president|cto|chief)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Management();

    [GeneratedRegex(@"(\.net|\bdotnet\b|\bc#|\bcsharp\b|\bengineer\w*\b|\bdeveloper\b|\bdevelopment\b|\bprogrammer\b|\bback[\s\-]?end\b|\bserver[\s\-]?side\b|\bplatform\b|\binfrastructure\b|\binfra\b|\bdevops\b|\bsre\b|\bsite\s*reliability\b|\barchitect\b|\bdistributed\b|\bsystems?\b|\bsoftware\b|\bcloud\b|\bkubernetes\b|\bapi\b|\bmicroservices?\b|\bgolang\b|\bjava\b|\bpython\b|\bnode(\.js)?\b|\brust\b|\bscala\b|\belixir\b|\bdatabase\b|\bcompiler\b|\btooling\b|\bdeveloper\s*(tools|experience)\b|\bdevex\b|\bllm\b|\bagent\w*\b|\bai\b)", RegexOptions.IgnoreCase)]
    private static partial Regex IncludeTerm();

    [GeneratedRegex(@"\b(senior|sr|staff|principal|lead|distinguished|architect)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SeniorLevel();
}
