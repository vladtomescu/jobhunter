using Anthropic.Models.Messages;
using JobHunter.Llm.Contracts;
using JobHunter.Llm.Exchange;
using JobHunter.Settings;

namespace JobHunter.Llm;

/// <summary>Writes one application kit through the model, lints what comes back and asks once more when the lint found something.</summary>
public sealed class AnthropicKitWriter(AnthropicClientFactory clientFactory, PromptCatalog prompts, ApiKeyDetector apiKeyDetector, SettingsService settingsService) : IKitWriter
{
    /// <summary>How long a written kit is allowed to be.</summary>
    public const int MaxTokens = 8192;

    /// <summary>The compensation stance the kit is written with; the numbers behind it never enter a prompt.</summary>
    public const string CompensationStance = "Compensation stance: open to discuss, on a line that carries a [CONFIRM] marker. No figure, no range, no currency, no start date, no notice period.";

    /// <inheritdoc />
    public async Task<KitOutcome> WriteAsync(KitRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!apiKeyDetector.IsPresent)
        {
            return KitOutcome.Failure(ApiKeyDetector.MissingKeyMessage, retryable: false);
        }

        Domain.Settings settings = await settingsService.GetAsync(cancellationToken);
        string systemPrompt = prompts.KitSystemPrompt(await ReadResumeAsync(settings.ResumeMarkdownPath, cancellationToken));
        string job = $"{LlmJson.ToLine(KitExchangeLine.From(request))}{Environment.NewLine}{Environment.NewLine}{CompensationStance}";

        KitAttempt first = await AttemptAsync(request, systemPrompt, job, cancellationToken);
        if (first.Payload is not KitPayload written)
        {
            return KitOutcome.Failure(first.FailureReason ?? "The kit could not be written.", first.Retryable);
        }

        IReadOnlyList<string> issues = KitLint.Inspect(written);
        if (issues.Count == 0)
        {
            return KitOutcome.Success(written, request.Model, first.Usage);
        }

        KitAttempt second = await AttemptAsync(request, systemPrompt, WithIssues(job, issues), cancellationToken);
        if (second.Payload is not KitPayload rewritten)
        {
            return KitOutcome.Success(written, request.Model, first.Usage, issues);
        }

        return KitOutcome.Success(rewritten, request.Model, Add(first.Usage, second.Usage), KitLint.Inspect(rewritten));
    }

    private static string WithIssues(string job, IReadOnlyList<string> issues)
    {
        string list = string.Join(Environment.NewLine, issues.Select(issue => $"- {issue}"));

        return $"{job}{Environment.NewLine}{Environment.NewLine}The previous attempt broke these rules. Write the kit again so that none of them remain:{Environment.NewLine}{list}";
    }

    private static LlmUsage Add(LlmUsage first, LlmUsage second)
    {
        return new LlmUsage(
            first.InputTokens + second.InputTokens,
            first.OutputTokens + second.OutputTokens,
            first.CacheReadTokens + second.CacheReadTokens,
            first.CacheCreationTokens + second.CacheCreationTokens);
    }

    private static async Task<string?> ReadResumeAsync(string? resumeMarkdownPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resumeMarkdownPath) || !File.Exists(resumeMarkdownPath))
        {
            return null;
        }

        try
        {
            return await File.ReadAllTextAsync(resumeMarkdownPath, cancellationToken);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<KitAttempt> AttemptAsync(KitRequest request, string systemPrompt, string userTurn, CancellationToken cancellationToken)
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
            OutputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = prompts.KitSchema } }
        };

        Message response;
        try
        {
            response = await clientFactory.Create().Messages.Create(parameters, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LlmFailureDescription failure = AnthropicFailure.Describe(exception);

            return new KitAttempt(null, LlmUsage.None, failure.Reason, failure.Retryable);
        }

        if (AnthropicResponse.Refusal(response) is string refusal)
        {
            return new KitAttempt(null, AnthropicResponse.Usage(response), refusal, false);
        }

        string text = AnthropicResponse.Text(response);
        if (string.IsNullOrWhiteSpace(text))
        {
            return new KitAttempt(null, AnthropicResponse.Usage(response), "The model returned no text for this kit.", true);
        }

        LlmJsonResult<KitPayload> parsed = LlmJson.Read<KitPayload>(text);

        return parsed.Payload is KitPayload payload
            ? new KitAttempt(payload, AnthropicResponse.Usage(response), null, false)
            : new KitAttempt(null, AnthropicResponse.Usage(response), $"The model returned a kit that does not match the schema: {parsed.Error}", false);
    }

    private sealed record KitAttempt(KitPayload? Payload, LlmUsage Usage, string? FailureReason, bool Retryable);
}
