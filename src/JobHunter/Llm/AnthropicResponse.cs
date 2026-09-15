using Anthropic.Models.Messages;
using JobHunter.Llm.Contracts;

namespace JobHunter.Llm;

/// <summary>Reads the three things this application wants out of a model response: a refusal, the text and what the call consumed.</summary>
public static class AnthropicResponse
{
    /// <summary>The refusal in words when the model declined to answer, otherwise null.</summary>
    public static string? Refusal(Message response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return response.StopDetails is RefusalStopDetails details
            ? $"The model declined to answer ({details.Category}): {details.Explanation}"
            : null;
    }

    /// <summary>Everything the model wrote as text, in the order the blocks arrived.</summary>
    public static string Text(Message response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return string.Concat(response.Content.Select(block => block.Value).OfType<TextBlock>().Select(block => block.Text)).Trim();
    }

    /// <summary>What the call consumed, including what the cached system block saved.</summary>
    public static LlmUsage Usage(Message response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return new LlmUsage(
            Count(response.Usage.InputTokens),
            Count(response.Usage.OutputTokens),
            Count(response.Usage.CacheReadInputTokens),
            Count(response.Usage.CacheCreationInputTokens));
    }

    private static int Count(long? value)
    {
        return value is long tokens && tokens > 0 ? (int)tokens : 0;
    }
}
