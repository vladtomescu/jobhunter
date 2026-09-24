using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Anthropic.Exceptions;

namespace JobHunter.Llm;

/// <summary>Why a model call failed, whether repeating it later is worth anything, and whether the account itself is out of allowance, which no further call of the same run can change.</summary>
public sealed record LlmFailureDescription(string Reason, bool Retryable, bool UsageLimitReached = false);

/// <summary>Turns an exception raised by the client into the failure outcome the rest of the application reads.</summary>
public static partial class AnthropicFailure
{
    /// <summary>The reason recorded when the account has reached its usage limit and the API names no date it returns.</summary>
    public const string UsageLimitReason = "Anthropic account usage limit reached";

    /// <summary>Maps one exception to its reason and to whether the next run should try the same call again; an API error is described by the message inside its response body, without the status line and the request identifier.</summary>
    public static LlmFailureDescription Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is AnthropicApiException apiException && ReadApiError(apiException) is ApiError apiError)
        {
            return IsUsageLimit(apiException, apiError)
                ? new LlmFailureDescription(DescribeUsageLimit(apiError.Message), Retryable: true, UsageLimitReached: true)
                : Describe(exception, apiError.Message);
        }

        return Describe(exception, exception.Message);
    }

    private static LlmFailureDescription Describe(Exception exception, string detail)
    {
        return exception switch
        {
            AnthropicRateLimitException => new LlmFailureDescription($"The model is rate limited: {detail}", true),
            Anthropic5xxException => new LlmFailureDescription($"The model is unavailable or overloaded: {detail}", true),
            AnthropicIOException => new LlmFailureDescription($"The model could not be reached: {detail}", true),
            AnthropicUnauthorizedException or AnthropicForbiddenException => new LlmFailureDescription($"The key was refused: {detail}", false),
            AnthropicBadRequestException or AnthropicUnprocessableEntityException => new LlmFailureDescription($"The request was rejected: {detail}", false),
            AnthropicApiException => new LlmFailureDescription($"The model returned an error: {detail}", false),
            TaskCanceledException or TimeoutException => new LlmFailureDescription($"The call timed out: {detail}", true),
            _ => new LlmFailureDescription($"The call failed: {detail}", false)
        };
    }

    /// <summary>True for the answer the API gives once the account's usage or spend limit is reached: a 400 invalid request whose message names the usage limit.</summary>
    private static bool IsUsageLimit(AnthropicApiException exception, ApiError error)
    {
        return exception.StatusCode == HttpStatusCode.BadRequest
            && string.Equals(error.Type, "invalid_request_error", StringComparison.Ordinal)
            && error.Message.Contains("usage limit", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The usage-limit reason in words, with the moment access returns when the API names it.</summary>
    private static string DescribeUsageLimit(string message)
    {
        Match regained = RegainAccessPattern().Match(message);

        return regained.Success
            ? $"{UsageLimitReason} — access returns {regained.Groups["date"].Value} {regained.Groups["time"].Value} UTC"
            : UsageLimitReason;
    }

    /// <summary>Reads the error type and message out of the JSON body the API answered with, or null when the body is missing or is not the API's error shape.</summary>
    private static ApiError? ReadApiError(AnthropicApiException exception)
    {
        if (string.IsNullOrWhiteSpace(exception.ResponseBody))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(exception.ResponseBody);

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("error", out JsonElement error)
                || error.ValueKind != JsonValueKind.Object
                || !error.TryGetProperty("message", out JsonElement message)
                || message.ValueKind != JsonValueKind.String
                || message.GetString() is not string text
                || string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            string? type = error.TryGetProperty("type", out JsonElement typeElement) && typeElement.ValueKind == JsonValueKind.String ? typeElement.GetString() : null;

            return new ApiError(type, text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"regain access on (?<date>\d{4}-\d{2}-\d{2}) at (?<time>\d{2}:\d{2}) UTC", RegexOptions.IgnoreCase)]
    private static partial Regex RegainAccessPattern();

    /// <summary>The error the API described in its response body.</summary>
    private sealed record ApiError(string? Type, string Message);
}
