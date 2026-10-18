namespace JobHunter.Pipeline;

/// <summary>The one lock every write to the skill catalog takes, so the writes run one at a time: a score recording its skills, a CV mark set by hand and a merge.</summary>
/// <remarks>Each write reads the catalog, changes it and saves it on a context of its own; without the shared lock a score that read a skill before the candidate marked it would save the model's mark over the candidate's, and a merge could interleave with a score naming the skill it deletes.</remarks>
public sealed class SkillCatalogGate
{
    private readonly SemaphoreSlim semaphore = new(1, 1);

    /// <summary>Runs one catalog write once every write before it has finished, and returns what it returns.</summary>
    public async Task<T> RunAsync<T>(Func<Task<T>> write, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);

        await semaphore.WaitAsync(cancellationToken);

        try
        {
            return await write();
        }
        finally
        {
            semaphore.Release();
        }
    }
}
