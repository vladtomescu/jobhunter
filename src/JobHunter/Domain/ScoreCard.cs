namespace JobHunter.Domain;

/// <summary>The rubric result for a job: seven dimensions scored 0-2, the facts extracted from the posting, and the provenance of the score.</summary>
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
    string DescriptionHashAtScoring);
