using JobHunter.Domain;

namespace JobHunter.Pipeline;

/// <summary>How far the CV covers the skills a posting leans on: how many of its primary skills the CV lacks or only comes close to and how many primary skills the posting names, how many of its required skills the CV lacks and how many required skills the posting names.</summary>
/// <remarks>Each skill is counted once whatever names it went by, with the mark the skill catalog holds for it; a skill nobody judged yet, or one the catalog does not carry, counts as covered, and the nice-to-have skills never count. The primary skills are read as alternatives, the way a posting lists several languages any one of which will do, so one covered primary skill is enough.</remarks>
public sealed record SkillCoverage(int PrimaryMarkedNo, int PrimaryMarkedClose, int PrimaryTotal, int RequiredMarkedNo, int RequiredTotal)
{
    private const string Primary = "primary";

    private const string Required = "required";

    /// <summary>True when the CV lacks every primary skill the posting names: it names at least one, and each is marked No.</summary>
    public bool LacksEveryPrimarySkill => PrimaryTotal > 0 && PrimaryMarkedNo == PrimaryTotal;

    /// <summary>True when the CV covers none of the primary skills but comes close to at least one of them.</summary>
    public bool OnlyComesCloseToThePrimarySkills => PrimaryMarkedClose > 0 && PrimaryMarkedNo + PrimaryMarkedClose == PrimaryTotal;

    /// <summary>True when the CV lacks most of the required skills: two or more of them, and more than half of the ones the posting names.</summary>
    public bool LacksMostRequiredSkills => RequiredMarkedNo >= 2 && RequiredMarkedNo * 2 > RequiredTotal;

    /// <summary>Resolves the skills on a score card through the catalog and counts the gaps; a card that names no skill has no coverage.</summary>
    public static SkillCoverage? Of(IReadOnlyCollection<ScoreSkill> skills, SkillCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(catalog);

        if (skills.Count == 0)
        {
            return null;
        }

        (string Importance, string Skill, CvMark? CvMark)[] named = [.. skills
            .Select(skill => (Importance: SkillCatalog.Normalize(skill.Importance), Skill: catalog.KeyOf(skill.Name), catalog.Resolve(skill.Name)?.CvMark))
            .Distinct()];

        return new SkillCoverage(
            Count(named, Primary, CvMark.No),
            Count(named, Primary, CvMark.Close),
            named.Count(entry => entry.Importance == Primary),
            Count(named, Required, CvMark.No),
            named.Count(entry => entry.Importance == Required));
    }

    private static int Count((string Importance, string Skill, CvMark? CvMark)[] named, string importance, CvMark cvMark)
    {
        return named.Count(entry => entry.Importance == importance && entry.CvMark == cvMark);
    }
}
