namespace JobHunter.Llm.Contracts;

/// <summary>Everything the scorer is told about one job; compensation settings are never part of a request.</summary>
public sealed record ScoreRequest(
    Guid JobId,
    string Title,
    string Company,
    string? Location,
    string? RemoteHint,
    string? EmploymentHint,
    string? CompText,
    DateTimeOffset? PostedAt,
    IReadOnlyList<string> Flags,
    string Description,
    string Model);

/// <summary>Everything the kit writer is told about one job, including the whole score payload and the class already computed from it.</summary>
public sealed record KitRequest(
    Guid JobId,
    string Title,
    string Company,
    string? ApplyUrl,
    string Description,
    ScorePayload Score,
    string Class,
    IReadOnlyList<string> Flags,
    string? LanguageHint,
    string Model);
