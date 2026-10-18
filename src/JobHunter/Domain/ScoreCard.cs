namespace JobHunter.Domain;

/// <summary>The rubric result for a job: seven dimensions scored 0-2, the facts extracted from the posting, and the provenance of the score.</summary>
/// <remarks>The skills are a nested owned collection, which the storage layer cannot bind to a constructor parameter, so they are set through the initializer; a card stored before skills were extracted was given an empty list and marked <see cref="ScoredBeforeSkills"/> by the AddSkills migration, so it reads with none and stays apart from a card whose posting names no skill.</remarks>
public sealed record ScoreCard(
    int Niche,
    int Level,
    int Stack,
    int RemoteTimezone,
    int ContractForm,
    int CompSignal,
    int CompanySignal,
    int Total,
    string Reasoning,
    string LevelGuess,
    string RemotePolicy,
    string EmploymentType,
    decimal? CompMin,
    decimal? CompMax,
    string? CompCurrency,
    CompPeriod? CompPeriod,
    string TimezoneNote,
    bool? RequiresUsAuthorization,
    bool? EndClientNamed,
    string AiMeaning,
    List<string> BlockingUnknowns,
    string Model,
    DateTimeOffset ScoredAt,
    string DescriptionHashAtScoring)
{
    /// <summary>The skills the posting asks for, as the model wrote them; the skill catalog resolves them to canonical skills when they are read.</summary>
    public List<ScoreSkill> Skills { get; init; } = [];

    /// <summary>The stack the model gave by the rubric, before the CV check recomputed it into <see cref="ScoreCard.Stack"/>; null on a card stored before it was kept.</summary>
    public int? ModelStack { get; init; }

    /// <summary>Whether the card was given before skills were extracted, so its empty skills list means not extracted rather than none named; only the AddSkills migration sets it, and a fresh score clears it.</summary>
    public bool ScoredBeforeSkills { get; init; }
}

/// <summary>One skill a posting asks for, exactly as the model wrote it: the raw name, how much the posting leans on it, its category and whether the model found it in the CV, each in the score schema's own words.</summary>
public sealed record ScoreSkill(string Name, string Importance, string Category, string CvMatch);
