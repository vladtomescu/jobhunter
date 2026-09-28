using JobHunter.Llm.Contracts;

namespace JobHunter.Llm;

/// <summary>Writes the cover letter for one saved job, from the same job input the kit is written from.</summary>
public interface ICoverLetterWriter
{
    /// <summary>Writes one cover letter and never throws: transport problems come back as a failure outcome.</summary>
    Task<CoverLetterOutcome> WriteAsync(KitRequest request, CancellationToken cancellationToken);
}
