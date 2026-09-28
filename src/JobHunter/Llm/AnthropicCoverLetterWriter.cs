using Anthropic.Models.Messages;
using JobHunter.Llm.Contracts;
using JobHunter.Llm.Exchange;
using JobHunter.Settings;

namespace JobHunter.Llm;

/// <summary>Writes one cover letter through the kit model, lints what comes back and asks once more when the lint found something.</summary>
public sealed class AnthropicCoverLetterWriter(AnthropicClientFactory clientFactory, PromptCatalog prompts, ApiKeyDetector apiKeyDetector, SettingsService settingsService) : ICoverLetterWriter
{
    /// <summary>How long a written letter is allowed to be.</summary>
    public const int MaxTokens = 8192;

    /// <inheritdoc />
    public async Task<CoverLetterOutcome> WriteAsync(KitRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!apiKeyDetector.IsPresent)
        {
            return CoverLetterOutcome.Failure(ApiKeyDetector.MissingKeyMessage, retryable: false);
        }

        Domain.Settings settings = await settingsService.GetAsync(cancellationToken);
        string systemPrompt = prompts.CoverLetterSystemPrompt(await ResumeMarkdown.ReadAsync(settings.ResumeMarkdownPath, cancellationToken));
        string job = LlmJson.ToLine(KitExchangeLine.From(request));

        LetterAttempt first = await AttemptAsync(request, systemPrompt, job, cancellationToken);
        if (first.Payload is not CoverLetterPayload written)
        {
            return CoverLetterOutcome.Failure(first.FailureReason ?? "The cover letter could not be written.", first.Retryable);
        }

        IReadOnlyList<string> issues = KitLint.Inspect(written);
        if (issues.Count == 0)
        {
            return CoverLetterOutcome.Success(written, request.Model, first.Usage);
        }

        LetterAttempt second = await AttemptAsync(request, systemPrompt, WithIssues(job, issues), cancellationToken);

        return second.Payload is CoverLetterPayload rewritten
            ? CoverLetterOutcome.Success(rewritten, request.Model, first.Usage.Plus(second.Usage))
            : CoverLetterOutcome.Success(written, request.Model, first.Usage);
    }

    private static string WithIssues(string job, IReadOnlyList<string> issues)
    {
        string list = string.Join(Environment.NewLine, issues.Select(issue => $"- {issue}"));

        return $"{job}{Environment.NewLine}{Environment.NewLine}The previous attempt broke these rules. Write the letter again so that none of them remain:{Environment.NewLine}{list}";
    }

    private async Task<LetterAttempt> AttemptAsync(KitRequest request, string systemPrompt, string userTurn, CancellationToken cancellationToken)
    {
        MessageCreateParams parameters = new()
        {
            Model = request.Model,
            MaxTokens = MaxTokens,
            System = new List<TextBlockParam>
            {
                new()
                {
                    Text = systemPrompt,
                    CacheControl = new CacheControlEphemeral()
                }
            },
            Messages = [new() { Role = Role.User, Content = userTurn }],
            OutputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = prompts.CoverLetterSchema } }
        };

        Message response;
        try
        {
            response = await clientFactory.Create().Messages.Create(parameters, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LlmFailureDescription failure = AnthropicFailure.Describe(exception);

            return new LetterAttempt(null, LlmUsage.None, failure.Reason, failure.Retryable);
        }

        if (AnthropicResponse.Refusal(response) is string refusal)
        {
            return new LetterAttempt(null, AnthropicResponse.Usage(response), refusal, false);
        }

        string text = AnthropicResponse.Text(response);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new LetterAttempt(null, AnthropicResponse.Usage(response), "The model returned no text for this cover letter.", true);
        }

        LlmJsonResult<CoverLetterPayload> parsed = LlmJson.Read<CoverLetterPayload>(text);

        return parsed.Payload is CoverLetterPayload payload
            ? new LetterAttempt(payload, AnthropicResponse.Usage(response), null, false)
            : new LetterAttempt(null, AnthropicResponse.Usage(response), $"The model returned a cover letter that does not match the schema: {parsed.Error}", false);
    }

    private sealed record LetterAttempt(CoverLetterPayload? Payload, LlmUsage Usage, string? FailureReason, bool Retryable);
}
