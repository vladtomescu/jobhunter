namespace JobHunter.Domain;

/// <summary>One canonical skill postings ask for, with the lowercased names that resolve to it, its category and whether the candidate's CV shows it.</summary>
/// <remarks>Jobs keep the skill names the model wrote; every read resolves them through the aliases, so a job is never rewritten when a skill gains an alias.</remarks>
public sealed class Skill
{
    private Skill()
    {
    }

    public int Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public List<string> Aliases { get; private set; } = [];

    public SkillCategory Category { get; private set; }

    public CvMark? CvMark { get; private set; }

    public CvMarkSetBy? CvMarkSetBy { get; private set; }

    /// <summary>Creates a skill the model named while scoring: its lowercased name is its first alias, and the model's judgement of the CV is its mark.</summary>
    public static Skill CreateFromModel(string name, SkillCategory category, CvMark cvMark)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string trimmed = name.Trim();

        return new Skill
        {
            Name = trimmed,
            Aliases = [trimmed.ToLowerInvariant()],
            Category = category,
            CvMark = cvMark,
            CvMarkSetBy = Domain.CvMarkSetBy.Model
        };
    }

    /// <summary>Takes the model's judgement of the CV while the skill carries no mark yet; a mark already set, by the model or by the candidate, is never overwritten.</summary>
    public void FillCvMarkFromModel(CvMark cvMark)
    {
        if (CvMark is not null)
        {
            return;
        }

        CvMark = cvMark;
        CvMarkSetBy = Domain.CvMarkSetBy.Model;
    }

    /// <summary>Takes every name of a skill merged into this one as an alias, its name lowercased and its own aliases, so the names on score cards that resolved to the merged skill resolve to this one; the category and the CV mark stay this skill's.</summary>
    public void TakeNamesOf(Skill merged)
    {
        ArgumentNullException.ThrowIfNull(merged);

        Aliases = [.. Aliases.Union([.. merged.Aliases, merged.Name.ToLowerInvariant()], StringComparer.Ordinal)];
    }

    /// <summary>Puts the candidate's own judgement of the CV on the skill, replacing whatever mark it carried.</summary>
    public void MarkByCandidate(CvMark cvMark)
    {
        CvMark = cvMark;
        CvMarkSetBy = Domain.CvMarkSetBy.Candidate;
    }
}
