using System.Diagnostics.CodeAnalysis;
using JobHunter.Domain;

namespace JobHunter.Refresh;

/// <summary>The one gate a refresh and a score run both pass, so that the two never overlap; a request made while either runs is refused, never queued.</summary>
public sealed class RunGate
{
    /// <summary>What a request is told while a refresh is running.</summary>
    public const string RefreshRunningMessage = "A refresh is already running; wait for it to finish.";

    /// <summary>What a request is told while a score run is running.</summary>
    public const string ScoreRunningMessage = "A score run is already running; wait for it to finish.";

    private readonly Lock gate = new();

    private FetchTrigger? holder;

    /// <summary>Takes the gate for a run of the given trigger, or reports why it cannot be taken because another run holds it.</summary>
    public bool TryEnter(FetchTrigger trigger, [NotNullWhen(false)] out string? refusal)
    {
        lock (gate)
        {
            if (holder is FetchTrigger running)
            {
                refusal = running == FetchTrigger.Score ? ScoreRunningMessage : RefreshRunningMessage;

                return false;
            }

            holder = trigger;
            refusal = null;

            return true;
        }
    }

    /// <summary>Releases the gate once the run that took it has finished.</summary>
    public void Exit()
    {
        lock (gate)
        {
            holder = null;
        }
    }
}
