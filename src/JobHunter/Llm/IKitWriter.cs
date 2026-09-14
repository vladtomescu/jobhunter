using JobHunter.Llm.Contracts;

namespace JobHunter.Llm;

/// <summary>Writes the application kit for one pursued job.</summary>
public interface IKitWriter
{
    /// <summary>Writes one kit and never throws: transport problems come back as a failure outcome.</summary>
    Task<KitOutcome> WriteAsync(KitRequest request, CancellationToken cancellationToken);
}
