using System.Text.Json.Serialization;

namespace JobHunter.Llm.Contracts;

/// <summary>The body of one cover letter exactly as the cover-letter schema defines it; the same shape arrives from the API path and from the exchange endpoint.</summary>
public sealed record CoverLetterPayload(
    [property: JsonPropertyName("job_id")] string JobId,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("salutation")] string Salutation,
    [property: JsonPropertyName("paragraphs")] List<string> Paragraphs,
    [property: JsonPropertyName("closing")] string Closing);
