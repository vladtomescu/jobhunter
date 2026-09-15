using System.Text.Encodings.Web;
using System.Text.Json;

namespace JobHunter.Llm;

/// <summary>The one way this application writes and reads the payloads that travel between it and the model.</summary>
public static class LlmJson
{
    /// <summary>Compact settings for what is sent out: one object per line, characters left as they are written.</summary>
    public static JsonSerializerOptions Writing { get; } = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Strict settings for what comes back: a missing property or a null in a place that cannot hold one is an error rather than a silent default.</summary>
    public static JsonSerializerOptions Reading { get; } = new()
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true
    };

    /// <summary>Writes one payload as a single line of compact JSON.</summary>
    public static string ToLine<TPayload>(TPayload payload)
    {
        return JsonSerializer.Serialize(payload, Writing);
    }

    /// <summary>Reads one payload from a line of JSON, returning the reason instead of throwing when the line does not fit the contract.</summary>
    public static LlmJsonResult<TPayload> Read<TPayload>(string json)
        where TPayload : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new LlmJsonResult<TPayload>(null, "the text is empty");
        }

        try
        {
            TPayload? payload = JsonSerializer.Deserialize<TPayload>(json, Reading);

            return payload is null
                ? new LlmJsonResult<TPayload>(null, "the text holds no object")
                : new LlmJsonResult<TPayload>(payload, null);
        }
        catch (JsonException exception)
        {
            return new LlmJsonResult<TPayload>(null, exception.Message);
        }
        catch (NotSupportedException exception)
        {
            return new LlmJsonResult<TPayload>(null, exception.Message);
        }
    }
}

/// <summary>A payload read from JSON, or the reason the text could not become one.</summary>
public sealed record LlmJsonResult<TPayload>(TPayload? Payload, string? Error)
    where TPayload : class;
