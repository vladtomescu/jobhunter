using Anthropic.Models.Messages;
using JobHunter.Llm.Contracts;
using JobHunter.Llm.Exchange;

namespace JobHunter.Llm;

/// <summary>Scores one job through the model: the profile and the rubric travel as a cached system block, the job as the user turn, the answer as structured output.</summary>
public sealed class AnthropicJobScorer(AnthropicClientFactory clientFactory, PromptCatalog prompts, ApiKeyDetector apiKeyDetector) : IJobScorer
{
    /// <summary>How long a scored answer is allowed to be.</summary>
    public const int MaxTokens = 4096;

    /// <inheritdoc />
    public async Task<ScoreOutcome> ScoreAsync(ScoreRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!apiKeyDetector.IsPresent)
        {
            return ScoreOutcome.Failure(ApiKeyDetector.MissingKeyMessage, retryable: false);
        }

        MessageCreateParams parameters = new()
        {
            Model = request.Model,
            MaxTokens = MaxTokens,
            System = new List<TextBlockParam>
            {
                new()
                {
                    Text = prompts.ScoringSystemPrompt,
                    CacheControl = new CacheControlEphemeral()
                }
            },
            Messages = [new() { Role = Role.User, Content = LlmJson.ToLine(ScoreExchangeLine.From(request)) }],
            OutputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = prompts.ScoreSchema } }
        };

        Message response;
        try
        {
            response = await clientFactory.Create().Messages.Create(parameters, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LlmFailureDescription failure = AnthropicFailure.Describe(exception);

            return failure.UsageLimitReached
                ? ScoreOutcome.UsageLimitFailure(failure.Reason)
                : ScoreOutcome.Failure(failure.Reason, failure.Retryable);
        }

        if (AnthropicResponse.Refusal(response) is string refusal)
        {
            return ScoreOutcome.Failure(refusal, retryable: false);
        }

        string text = AnthropicResponse.Text(response);
        if (string.IsNullOrWhiteSpace(text))
        {
            return ScoreOutcome.Failure("The model returned no text for this job.", retryable: true);
        }

        LlmJsonResult<ScorePayload> parsed = LlmJson.Read<ScorePayload>(text);

        return parsed.Payload is ScorePayload payload
            ? ScoreOutcome.Success(payload, request.Model, AnthropicResponse.Usage(response))
            : ScoreOutcome.Failure($"The model returned a score that does not match the schema: {parsed.Error}", retryable: false);
    }
}
