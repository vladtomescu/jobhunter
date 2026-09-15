using Anthropic;

namespace JobHunter.Llm;

/// <summary>Hands out the one client the application talks to the model with, built from the environment key the first time it is asked for.</summary>
/// <remarks>The client owns its connections, so it is created once and shared; retries and their backoff are left to the client itself.</remarks>
public sealed class AnthropicClientFactory(ApiKeyDetector apiKeyDetector)
{
    /// <summary>How often a call is repeated inside the client before the failure reaches the caller.</summary>
    public const int MaxRetries = 3;

    private readonly Lock gate = new();
    private AnthropicClient? client;

    /// <summary>Returns the shared client, creating it on first use; it throws when no key is available, so callers check the detector first.</summary>
    public AnthropicClient Create()
    {
        string apiKey = apiKeyDetector.Read() ?? throw new InvalidOperationException(ApiKeyDetector.MissingKeyMessage);

        lock (gate)
        {
            return client ??= new AnthropicClient
            {
                ApiKey = apiKey,
                MaxRetries = MaxRetries
            };
        }
    }
}
