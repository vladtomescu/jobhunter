using JobHunter.Llm;
using JobHunter.Llm.Contracts;

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

    public Task<ScoreOutcome> ScoreAsync(ScoreRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (gate)
        {
            requests.Add(request);
        }

        ScoreOutcome outcome = Answer is null
            ? ScoreOutcome.Success(StrongPayload(request.JobId), ModelName, LlmUsage.None)
            : Answer(request);

        return Task.FromResult(outcome);
    }
}
