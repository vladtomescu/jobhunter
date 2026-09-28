namespace JobHunter.Llm.Contracts;

/// <summary>What one model call consumed, as reported by the API; zero everywhere on the exchange path.</summary>
public sealed record LlmUsage(int InputTokens, int OutputTokens, int CacheReadTokens, int CacheCreationTokens)
{
    /// <summary>Usage for a result that did not come from an API call.</summary>
    public static LlmUsage None { get; } = new(0, 0, 0, 0);

    /// <summary>What this call and another one consumed together, as a writer reports a first attempt and its rewrite.</summary>
    public LlmUsage Plus(LlmUsage other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return new LlmUsage(
            InputTokens + other.InputTokens,
            OutputTokens + other.OutputTokens,
            CacheReadTokens + other.CacheReadTokens,
            CacheCreationTokens + other.CacheCreationTokens);
    }
}

/// <summary>The result of one model call: either a payload with its model and usage, or a failure that says whether retrying is worth it.</summary>
public abstract record LlmOutcome<TPayload>
    where TPayload : class
{
    private protected LlmOutcome(TPayload? payload, string? model, LlmUsage? usage, string? failureReason, bool retryable)
    {
        Payload = payload;
        Model = model;
        Usage = usage;
        FailureReason = failureReason;
        Retryable = retryable;
    }

    /// <summary>The result of the call, present only when the call succeeded.</summary>
    public TPayload? Payload { get; }

    /// <summary>The model that produced the payload, or claude-code when the payload came through the exchange files.</summary>
    public string? Model { get; }

    /// <summary>Tokens consumed by the call.</summary>
    public LlmUsage? Usage { get; }

    /// <summary>Why the call failed, in words that can be shown in the interface.</summary>
    public string? FailureReason { get; }

    /// <summary>True when the same call is worth repeating later, for instance after a rate limit.</summary>
    public bool Retryable { get; }

    /// <summary>True when the call produced a payload.</summary>
    public bool IsSuccess => Payload is not null;
}

/// <summary>The result of scoring one job.</summary>
public sealed record ScoreOutcome : LlmOutcome<ScorePayload>
{
    private ScoreOutcome(ScorePayload? payload, string? model, LlmUsage? usage, string? failureReason, bool retryable, bool usageLimitReached)
        : base(payload, model, usage, failureReason, retryable)
    {
        UsageLimitReached = usageLimitReached;
    }

    /// <summary>True when the call was refused because the account reached its usage limit, so every further call of the run would be refused the same way.</summary>
    public bool UsageLimitReached { get; }

    /// <summary>A scored job.</summary>
    public static ScoreOutcome Success(ScorePayload payload, string model, LlmUsage usage)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return new ScoreOutcome(payload, model, usage, null, false, false);
    }

    /// <summary>A scoring call that did not produce a usable payload.</summary>
    public static ScoreOutcome Failure(string reason, bool retryable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new ScoreOutcome(null, null, null, reason, retryable, false);
    }

    /// <summary>A scoring call the API refused because the account reached its usage limit; the job itself is not at fault and is worth sending again once access returns.</summary>
    public static ScoreOutcome UsageLimitFailure(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new ScoreOutcome(null, null, null, reason, true, true);
    }
}

/// <summary>The result of writing one application kit.</summary>
public sealed record KitOutcome : LlmOutcome<KitPayload>
{
    private KitOutcome(KitPayload? payload, string? model, LlmUsage? usage, string? failureReason, bool retryable, IReadOnlyList<string> lintIssues)
        : base(payload, model, usage, failureReason, retryable)
    {
        LintIssues = lintIssues;
    }

    /// <summary>The lint findings that remain on the written kit after the automatic regeneration; empty when the kit is clean and always empty on a failure.</summary>
    public IReadOnlyList<string> LintIssues { get; }

    /// <summary>A written kit that passed the lint.</summary>
    public static KitOutcome Success(KitPayload payload, string model, LlmUsage usage)
    {
        return Success(payload, model, usage, []);
    }

    /// <summary>A written kit with the lint findings that remain after the automatic regeneration.</summary>
    public static KitOutcome Success(KitPayload payload, string model, LlmUsage usage, IReadOnlyList<string> lintIssues)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(lintIssues);

        return new KitOutcome(payload, model, usage, null, false, lintIssues);
    }

    /// <summary>A kit call that did not produce a usable payload.</summary>
    public static KitOutcome Failure(string reason, bool retryable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new KitOutcome(null, null, null, reason, retryable, []);
    }
}

/// <summary>The result of writing one cover letter; the lint runs again when the letter is stored, so the outcome carries none of its findings.</summary>
public sealed record CoverLetterOutcome : LlmOutcome<CoverLetterPayload>
{
    private CoverLetterOutcome(CoverLetterPayload? payload, string? model, LlmUsage? usage, string? failureReason, bool retryable)
        : base(payload, model, usage, failureReason, retryable)
    {
    }

    /// <summary>A written cover letter.</summary>
    public static CoverLetterOutcome Success(CoverLetterPayload payload, string model, LlmUsage usage)
    {
        ArgumentNullException.ThrowIfNull(payload);

        return new CoverLetterOutcome(payload, model, usage, null, false);
    }

    /// <summary>A cover-letter call that did not produce a usable payload.</summary>
    public static CoverLetterOutcome Failure(string reason, bool retryable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new CoverLetterOutcome(null, null, null, reason, retryable);
    }
}
