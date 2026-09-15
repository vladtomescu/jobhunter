using JobHunter.Llm;
using JobHunter.Llm.Contracts;

namespace JobHunter.Tests.Applications;

/// <summary>An in-memory kit writer that records every request it receives and returns a scripted outcome, so tests never touch the network.</summary>
public sealed class FakeKitWriter : IKitWriter
{
    private readonly Queue<Func<KitRequest, KitOutcome>> scriptedOutcomes = new();

    /// <summary>Every request handed to WriteAsync, in call order.</summary>
    public List<KitRequest> Requests { get; } = [];

    /// <summary>How many times WriteAsync was called.</summary>
    public int CallCount => Requests.Count;

    /// <summary>The outcome returned when nothing is scripted for the next call.</summary>
    public KitOutcome DefaultOutcome { get; set; } = KitOutcome.Success(
        new KitPayload("job", "en", ["Backend-heavy stack.", "Senior scope.", "Remote-first team."], "Cover note in a plain, direct voice.",
            [new KitAnswerPayload("Why this company", "The platform work lines up with what I already do.")],
            ["What does the first month look like"]),
        "claude-opus-5",
        LlmUsage.None);

    /// <summary>When set, the next call throws this instead of returning an outcome.</summary>
    public Exception? ExceptionToThrowOnNextCall { get; set; }

    /// <summary>Queues an outcome for the next call, ahead of DefaultOutcome.</summary>
    public void EnqueueOutcome(Func<KitRequest, KitOutcome> outcomeFactory)
    {
        scriptedOutcomes.Enqueue(outcomeFactory);
    }

    public Task<KitOutcome> WriteAsync(KitRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        if (ExceptionToThrowOnNextCall is Exception exception)
        {
            ExceptionToThrowOnNextCall = null;

            throw exception;
        }

        KitOutcome outcome = scriptedOutcomes.Count > 0 ? scriptedOutcomes.Dequeue()(request) : DefaultOutcome;

        return Task.FromResult(outcome);
    }
}
