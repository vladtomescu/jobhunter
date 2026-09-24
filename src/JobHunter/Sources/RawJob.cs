using JobHunter.Domain;
using JobHunter.Pipeline;

namespace JobHunter.Sources;

/// <summary>The sources a job can come from; Manual covers jobs entered by hand.</summary>
public enum JobSourceKind
{
    RemoteOk,
    WeWorkRemotely,
    Dataset,
    Manual
}

/// <summary>A posting exactly as a source reports it, before normalization, deduplication and prefiltering.</summary>
public sealed record RawJob(
    JobSourceKind Source,
    string SourceId,
    string Title,
    string Company,
    string? CompanyUrl,
    string PostingUrl,
    string? ApplyUrl,
    string DescriptionRaw,
    string? LocationText,
    string? CountryIso,
    string? RegionText,
    bool? IsRemote,
    string? Language,
    decimal? CompMin,
    decimal? CompMax,
    string? CompCurrency,
    CompPeriod? CompPeriod,
    string? CompSummary,
    string? EmploymentType,
    string? Ats,
    IReadOnlyList<string> Tags,
    DateTimeOffset? PostedAt);

/// <summary>What a source is given for one fetch: the intake window, the candidate profile of the run (title rules and accepted languages), where to cache raw responses and the current settings.</summary>
/// <remarks>The settings type is written qualified because the JobHunter.Settings namespace shadows the plain name inside this namespace.</remarks>
public sealed record SourceFetchContext(DateTimeOffset NotBefore, CandidateProfile Candidate, string RawCacheFolder, Domain.Settings Settings);

/// <summary>What a source returns from one fetch; a source reports failure through Error instead of throwing.</summary>
public sealed record SourceFetchResult(IReadOnlyList<RawJob> Jobs, int FetchedCount, string? Error);
