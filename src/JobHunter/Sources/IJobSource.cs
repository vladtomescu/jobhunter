namespace JobHunter.Sources;

/// <summary>One place jobs are fetched from; every source reads a single feed per run and reports its own errors.</summary>
public interface IJobSource
{
    /// <summary>Which source this is.</summary>
    JobSourceKind Kind { get; }

    /// <summary>True when one fetch returns every live job of the source, which is what makes the liveness pass meaningful.</summary>
    bool IsFullSnapshot { get; }

    /// <summary>Fetches the postings currently offered by this source.</summary>
    Task<SourceFetchResult> FetchAsync(SourceFetchContext context, CancellationToken cancellationToken);
}
