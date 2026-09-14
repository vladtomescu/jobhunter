using System.Text.Json.Serialization;

namespace JobHunter.Llm.Contracts;

/// <summary>One standard application question with the answer written for it.</summary>
public sealed record KitAnswerPayload(
    [property: JsonPropertyName("question")] string Question,
    [property: JsonPropertyName("answer")] string Answer);

/// <summary>One application kit exactly as the kit schema defines it; the same shape arrives from the API path and from the exchange files.</summary>
public sealed record KitPayload(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("fit_summary")] List<string> FitSummary,
    [property: JsonPropertyName("cover_note")] string CoverNote,
    [property: JsonPropertyName("ats_answers")] List<KitAnswerPayload> AtsAnswers,
    [property: JsonPropertyName("call_questions")] List<string> CallQuestions);
