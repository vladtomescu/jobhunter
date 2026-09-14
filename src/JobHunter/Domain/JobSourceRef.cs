using JobHunter.Sources;

namespace JobHunter.Domain;

/// <summary>One source that carries this job, with the moment that source last listed it.</summary>
public sealed record JobSourceRef(JobSourceKind Kind, string SourceId, DateTimeOffset LastSeenAt);
