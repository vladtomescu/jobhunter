using JobHunter.Llm.Contracts;

namespace JobHunter.Llm;

/// <summary>Scores one job against the rubric; the implementation decides the transport, the classification always happens in code.</summary>
public interface IJobScorer
{
    /// <summary>Scores one job and never throws: transport problems come back as a failure outcome.</summary>
    Task<ScoreOutcome> ScoreAsync(ScoreRequest request, CancellationToken cancellationToken);
}
