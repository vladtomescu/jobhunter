using System.Collections.Concurrent;

namespace JobHunter.Llm.Exchange;

/// <summary>The jobs handed out in a to-score batch and not scored yet: a claim keeps a job out of every other batch for 30 minutes, or until a score for it is imported, so two batches never score the same job.</summary>
/// <remarks>The claims live in memory, because the app is a single process; a restart frees them all.</remarks>
public sealed class ScoreClaims(TimeProvider timeProvider)
{
    /// <summary>How long a claim keeps a job out of other batches.</summary>
    public static readonly TimeSpan Lease = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<Guid, DateTimeOffset> expiries = new();

    /// <summary>Claims the job for one lease; false when a live claim already holds it. A claim that expired is taken over.</summary>
    public bool TryClaim(Guid jobId)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        DateTimeOffset expiry = now + Lease;

        while (true)
        {
            if (expiries.TryAdd(jobId, expiry))
            {
                return true;
            }

            if (!expiries.TryGetValue(jobId, out DateTimeOffset heldUntil))
            {
                continue;
            }

            if (heldUntil > now)
            {
                return false;
            }

            if (expiries.TryUpdate(jobId, expiry, heldUntil))
            {
                return true;
            }
        }
    }

    /// <summary>Frees the job's claim, once a score for it is stored.</summary>
    public void Release(Guid jobId)
    {
        expiries.TryRemove(jobId, out _);
    }
}
