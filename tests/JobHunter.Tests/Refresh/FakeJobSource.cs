using JobHunter.Sources;

namespace JobHunter.Tests.Refresh;

/// <summary>A source that returns whatever a test hands it, records the context it was fetched with, and can fail or block on demand.</summary>
internal sealed class FakeJobSource(JobSourceKind kind, bool isFullSnapshot = false) : IJobSource
{
    private readonly Queue<SourceFetchResult> results = new();

    /// <summary>Which source this stands in for.</summary>
    public JobSourceKind Kind { get; } = kind;

    /// <summary>Whether one fetch returns everything the source offers, which is what makes the liveness pass apply.</summary>
    public bool IsFullSnapshot { get; } = isFullSnapshot;

    /// <summary>Every context the source was fetched with, in call order.</summary>
    public List<SourceFetchContext> Contexts { get; } = [];

    /// <summary>Thrown by the next fetch instead of returning a result.</summary>
    public Exception? ExceptionToThrow { get; set; }

    /// <summary>Awaited before the fetch returns, which lets a test hold a run open.</summary>
    public Func<Task>? Gate { get; set; }

    /// <summary>Queues what the next fetch returns; a fetch with nothing queued returns no postings.</summary>
    public void Returns(params RawJob[] jobs)
    {
        results.Enqueue(new SourceFetchResult(jobs, jobs.Length, null));
    }

    /// <summary>Queues a fetch that returns postings alongside an error line, the way a source with several feeds does.</summary>
    public void ReturnsWithError(string error, params RawJob[] jobs)
    {
        results.Enqueue(new SourceFetchResult(jobs, jobs.Length, error));
    }

    public async Task<SourceFetchResult> FetchAsync(SourceFetchContext context, CancellationToken cancellationToken)
    {
        Contexts.Add(context);

        if (Gate is Func<Task> gate)
        {
            await gate();
        }

        if (ExceptionToThrow is Exception exception)
        {
            throw exception;
        }

        return results.Count > 0 ? results.Dequeue() : new SourceFetchResult([], 0, null);
    }
}
