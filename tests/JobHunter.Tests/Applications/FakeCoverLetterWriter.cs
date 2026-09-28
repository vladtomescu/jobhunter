using JobHunter.Llm;
using JobHunter.Llm.Contracts;

namespace JobHunter.Tests.Applications;

/// <summary>An in-memory cover-letter writer that records every request it receives and returns a scripted outcome, so tests never touch the network.</summary>
public sealed class FakeCoverLetterWriter : ICoverLetterWriter
{
    private readonly Queue<Func<KitRequest, CoverLetterOutcome>> scriptedOutcomes = new();

    /// <summary>Every request handed to WriteAsync, in call order.</summary>
    public List<KitRequest> Requests { get; } = [];

    /// <summary>How many times WriteAsync was called.</summary>
    public int CallCount => Requests.Count;

    /// <summary>When set, the next call throws this instead of returning an outcome.</summary>
    public Exception? ExceptionToThrowOnNextCall { get; set; }

    /// <summary>A clean letter for the requested job, as the writer returns it when nothing is scripted.</summary>
    public static CoverLetterPayload CleanLetter(Guid jobId)
    {
        return new CoverLetterPayload(
            jobId.ToString(),
            "en",
            "Dear Example Co team,",
            [
                "I am writing about the Backend Engineer role at Example Co, whose posting describes the platform work I do now.",
                "I own the ledger service that settles 40 million transactions a day, and I led its move to an event-driven pipeline.",
                "I would welcome a conversation about how your team runs its platform today."
            ],
            "Kind regards,");
    }

    /// <summary>Queues an outcome for the next call, ahead of the clean letter.</summary>
    public void EnqueueOutcome(Func<KitRequest, CoverLetterOutcome> outcomeFactory)
    {
        scriptedOutcomes.Enqueue(outcomeFactory);
    }

    public Task<CoverLetterOutcome> WriteAsync(KitRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        if (ExceptionToThrowOnNextCall is Exception exception)
        {
            ExceptionToThrowOnNextCall = null;

            throw exception;
        }

        CoverLetterOutcome outcome = scriptedOutcomes.Count > 0
            ? scriptedOutcomes.Dequeue()(request)
            : CoverLetterOutcome.Success(CleanLetter(request.JobId), request.Model, LlmUsage.None);

        return Task.FromResult(outcome);
    }
}
