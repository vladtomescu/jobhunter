using System.Net;
using JobHunter.Llm;
using JobHunter.Llm.Contracts;
using JobHunter.Tests.Llm;

namespace JobHunter.Tests.Refresh;

/// <summary>A scorer that answers from memory, so that a refresh test never reaches the network.</summary>
internal sealed class FakeJobScorer : IJobScorer
{
    private readonly Lock gate = new();

    private readonly List<ScoreRequest> requests = [];

    /// <summary>The model name the fake reports back.</summary>
    public const string ModelName = "fake-scorer";

    /// <summary>Decides what one request answers with; without it every request is scored well.</summary>
    public Func<ScoreRequest, ScoreOutcome>? Answer { get; set; }

    /// <summary>Decides what one request answers with when the answer must wait, which lets a test hold calls in flight; it takes precedence over the synchronous answer.</summary>
    public Func<ScoreRequest, Task<ScoreOutcome>>? AnswerAsync { get; set; }

    /// <summary>Every request the scorer received, in the order the calls arrived.</summary>
    public IReadOnlyList<ScoreRequest> Requests
    {
        get
        {
            lock (gate)
            {
                return [.. requests];
            }
        }
    }

    /// <summary>A payload that classifies as a strong job, addressed to one job.</summary>
    public static ScorePayload StrongPayload(Guid jobId)
    {
        return new ScorePayload(
            jobId.ToString(),
            new ScoreDimensionsPayload(2, 2, 2, 2, 2, 1, 2),
            new ScoreFactsPayload("senior", "remote", "either", new ScoreCompPayload(null, null, null, null), "Overlaps European hours.", false, true, "Agent tooling."),
            [],
            "Platform work in the stack the profile names.");
    }

    /// <summary>The outcome the real scorer returns once the account reached its usage limit, described from the exception the client raises for that answer.</summary>
    public static ScoreOutcome UsageLimitOutcome()
    {
        LlmFailureDescription failure = AnthropicFailure.Describe(AnthropicErrors.Exception(HttpStatusCode.BadRequest, AnthropicErrors.UsageLimitBody));

        return failure.UsageLimitReached
            ? ScoreOutcome.UsageLimitFailure(failure.Reason)
            : throw new InvalidOperationException("The usage-limit answer was not recognised as the usage limit.");
    }

    public Task<ScoreOutcome> ScoreAsync(ScoreRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (gate)
        {
            requests.Add(request);
        }

        if (AnswerAsync is Func<ScoreRequest, Task<ScoreOutcome>> answerAsync)
        {
            return answerAsync(request);
        }

        ScoreOutcome outcome = Answer is null
            ? ScoreOutcome.Success(StrongPayload(request.JobId), ModelName, LlmUsage.None)
            : Answer(request);

        return Task.FromResult(outcome);
    }
}
