using Anthropic.Exceptions;

namespace JobHunter.Llm;

/// <summary>Why a model call failed and whether repeating it later is worth anything.</summary>
public sealed record LlmFailureDescription(string Reason, bool Retryable);

/// <summary>Turns an exception raised by the client into the failure outcome the rest of the application reads.</summary>
public static class AnthropicFailure
{
    /// <summary>Maps one exception to its reason and to whether the next run should try the same call again.</summary>
    public static LlmFailureDescription Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            AnthropicRateLimitException => new LlmFailureDescription($"The model is rate limited: {exception.Message}", true),
            Anthropic5xxException => new LlmFailureDescription($"The model is unavailable or overloaded: {exception.Message}", true),
            AnthropicIOException => new LlmFailureDescription($"The model could not be reached: {exception.Message}", true),
            AnthropicUnauthorizedException or AnthropicForbiddenException => new LlmFailureDescription($"The key was refused: {exception.Message}", false),
            AnthropicBadRequestException or AnthropicUnprocessableEntityException => new LlmFailureDescription($"The request was rejected: {exception.Message}", false),
            AnthropicApiException => new LlmFailureDescription($"The model returned an error: {exception.Message}", false),
            TaskCanceledException or TimeoutException => new LlmFailureDescription($"The call timed out: {exception.Message}", true),
            _ => new LlmFailureDescription($"The call failed: {exception.Message}", false)
        };
    }
}
