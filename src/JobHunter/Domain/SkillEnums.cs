namespace JobHunter.Domain;

/// <summary>The kind of skill a posting asks for.</summary>
public enum SkillCategory
{
    Language,
    Framework,
    Cloud,
    Database,
    Tool,
    Practice,
    Unsorted
}

/// <summary>Whether the candidate's CV shows a skill: yes, something close to it, or not at all.</summary>
public enum CvMark
{
    Yes,
    Close,
    No
}

/// <summary>Who put the CV mark on a skill: the model while scoring, or the candidate by hand.</summary>
public enum CvMarkSetBy
{
    Model,
    Candidate
}
