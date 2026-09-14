using System.Text.Json.Serialization;

namespace JobHunter.Llm.Contracts;

/// <summary>The seven rubric dimensions, each scored 0 to 2.</summary>
public sealed record ScoreDimensionsPayload(
    [property: JsonPropertyName("niche")] int Niche,
    [property: JsonPropertyName("level")] int Level,
    [property: JsonPropertyName("stack")] int Stack,
    [property: JsonPropertyName("remote_timezone")] int RemoteTimezone,
    [property: JsonPropertyName("contract_form")] int ContractForm,
    [property: JsonPropertyName("comp_signal")] int CompSignal,
    [property: JsonPropertyName("company_signal")] int CompanySignal);

/// <summary>Compensation as stated in the posting, left null where the posting says nothing.</summary>
public sealed record ScoreCompPayload(
    [property: JsonPropertyName("min")] decimal? Min,
    [property: JsonPropertyName("max")] decimal? Max,
    [property: JsonPropertyName("currency")] string? Currency,
    [property: JsonPropertyName("period")] string? Period);

/// <summary>The facts the model extracts from the posting alongside the scores.</summary>
public sealed record ScoreFactsPayload(
    [property: JsonPropertyName("level_guess")] string LevelGuess,
    [property: JsonPropertyName("remote_policy")] string RemotePolicy,
    [property: JsonPropertyName("employment_type")] string EmploymentType,
    [property: JsonPropertyName("comp")] ScoreCompPayload Comp,
    [property: JsonPropertyName("timezone_note")] string TimezoneNote,
    [property: JsonPropertyName("requires_us_authorization")] bool? RequiresUsAuthorization,
    [property: JsonPropertyName("end_client_named")] bool? EndClientNamed,
    [property: JsonPropertyName("ai_meaning")] string AiMeaning);

/// <summary>One scored job exactly as the score schema defines it; the same shape arrives from the API path and from the exchange files.</summary>
public sealed record ScorePayload(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("scores")] ScoreDimensionsPayload Scores,
    [property: JsonPropertyName("facts")] ScoreFactsPayload Facts,
    [property: JsonPropertyName("blocking_unknowns")] List<string> BlockingUnknowns,
    [property: JsonPropertyName("reasoning")] string Reasoning);
