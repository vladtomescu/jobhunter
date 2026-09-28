using JobHunter.Sources;

namespace JobHunter.Domain;

/// <summary>One source that carries this job, with the moment that source last listed it and the hash of the description its posting carried then; the hash stays null until a refresh records one, which it never does for a job entered by hand.</summary>
public sealed record JobSourceRef(JobSourceKind Kind, string SourceId, DateTimeOffset LastSeenAt, string? DescriptionHash = null);
