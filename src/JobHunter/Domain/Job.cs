using JobHunter.Sources;

namespace JobHunter.Domain;

/// <summary>A job posting gathered from one or more sources, carrying its prefilter verdict, score, class and triage state.</summary>
public sealed class Job
{
    private Job()
    {
    }

    public Guid Id { get; private set; }

    public string Fingerprint { get; private set; } = string.Empty;

    public string CanonicalApplyUrl { get; private set; } = string.Empty;

    public string? ApplyUrl { get; private set; }

    public string PostingUrl { get; private set; } = string.Empty;

    public List<JobSourceRef> Sources { get; private set; } = [];

    public string Company { get; private set; } = string.Empty;

    public string? CompanyUrl { get; private set; }

    public AtsKind? Ats { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string DescriptionText { get; private set; } = string.Empty;

    public string DescriptionHash { get; private set; } = string.Empty;

    public List<string> Tags { get; private set; } = [];

    public string? LocationText { get; private set; }

    public string? CountryIso { get; private set; }

    public string? RegionText { get; private set; }

    public bool? IsRemoteFromSource { get; private set; }

    public string? Language { get; private set; }

    public string? EmploymentTypeFromSource { get; private set; }

    public decimal? CompMin { get; private set; }

    public decimal? CompMax { get; private set; }

    public string? CompCurrency { get; private set; }

    public CompPeriod? CompPeriod { get; private set; }

    public decimal? CompMinPerYear { get; private set; }

    public decimal? CompMaxPerYear { get; private set; }

    public DateTimeOffset? PostedAt { get; private set; }

    public DateTimeOffset FirstSeenAt { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    public bool IsActive { get; private set; }

    public int MissedRuns { get; private set; }

    public bool IsManual { get; private set; }

    public PrefilterState Prefilter { get; private set; }

    public string? DropReason { get; private set; }

    public List<JobFlag> Flags { get; private set; } = [];

    public ScoringState Scoring { get; private set; }

    public string? ScoreError { get; private set; }

    public ScoreCard? Score { get; private set; }

    public JobClass? Class { get; private set; }

    public TriageState Triage { get; private set; }

    public DateTimeOffset? TriagedAt { get; private set; }

    /// <summary>Creates a job at its first sighting; every other fact is recorded afterwards through the methods below.</summary>
    public static Job Create(string fingerprint, string canonicalApplyUrl, string postingUrl, string company, string title, string descriptionText, string descriptionHash, DateTimeOffset seenAt, bool isManual)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalApplyUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(postingUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(company);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return new Job
        {
            Id = Guid.CreateVersion7(),
            Fingerprint = fingerprint,
            CanonicalApplyUrl = canonicalApplyUrl,
            PostingUrl = postingUrl,
            Company = company,
            Title = title,
            DescriptionText = descriptionText,
            DescriptionHash = descriptionHash,
            FirstSeenAt = seenAt,
            LastSeenAt = seenAt,
            IsActive = true,
            IsManual = isManual,
            Prefilter = PrefilterState.Pending,
            Scoring = ScoringState.Unscored,
            Triage = TriageState.New
        };
    }

    /// <summary>Records that a source listed this job now; an unknown source is added, a known one has its sighting moved forward.</summary>
    public void RecordSource(JobSourceKind kind, string sourceId, DateTimeOffset seenAt)
    {
        int index = Sources.FindIndex(source => source.Kind == kind && source.SourceId == sourceId);
        if (index >= 0)
        {
            Sources[index] = Sources[index] with { LastSeenAt = seenAt };
        }
        else
        {
            Sources.Add(new JobSourceRef(kind, sourceId, seenAt));
        }

        LastSeenAt = seenAt;
        MissedRuns = 0;
        IsActive = true;
    }

    /// <summary>Records the facts a source reports about the posting itself; a null argument leaves the value already known untouched.</summary>
    public void RecordPostingFacts(string? applyUrl, string? companyUrl, AtsKind? ats, IEnumerable<string> tags, string? employmentTypeFromSource, DateTimeOffset? postedAt)
    {
        ApplyUrl = applyUrl ?? ApplyUrl;
        CompanyUrl = companyUrl ?? CompanyUrl;
        Ats = ats ?? Ats;
        EmploymentTypeFromSource = employmentTypeFromSource ?? EmploymentTypeFromSource;
        PostedAt = postedAt ?? PostedAt;

        foreach (string tag in tags)
        {
            if (!Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                Tags.Add(tag);
            }
        }
    }

    /// <summary>Records where the role sits and which language the posting is written in; a null argument leaves the value already known untouched.</summary>
    public void RecordPlace(string? locationText, string? countryIso, string? regionText, bool? isRemoteFromSource, string? language)
    {
        LocationText = locationText ?? LocationText;
        CountryIso = countryIso ?? CountryIso;
        RegionText = regionText ?? RegionText;
        IsRemoteFromSource = isRemoteFromSource ?? IsRemoteFromSource;
        Language = language ?? Language;
    }

    /// <summary>Records the compensation stated by the source together with its normalization to the base currency per year, and refreshes the high-pay flag against the given threshold; a threshold left null leaves the flag off.</summary>
    public void RecordCompensation(decimal? compMin, decimal? compMax, string? currency, CompPeriod? period, decimal? minPerYear, decimal? maxPerYear, decimal? highPayThresholdPerYear = null)
    {
        CompMin = compMin;
        CompMax = compMax;
        CompCurrency = currency;
        CompPeriod = period;
        CompMinPerYear = minPerYear;
        CompMaxPerYear = maxPerYear;
        RefreshHighPayFlag(highPayThresholdPerYear);
    }

    /// <summary>Clears the compensation-unknown flag once a figure is known, whoever found it; the flag is meaningless next to a stated figure.</summary>
    public void ClearCompensationUnknownFlag()
    {
        Flags.Remove(JobFlag.CU);
    }

    /// <summary>Records whether the posting requires United States work authorization, raising the flag that says so or clearing it when a later reading of the posting settles the other way.</summary>
    public void RecordWorkAuthorizationRequirement(bool isRequired)
    {
        RaiseOrClear(JobFlag.WA, isRequired);
    }

    /// <summary>Records whether the role mentions one of the candidate's stack keywords, raising the flag that says so or clearing it when the posting no longer does.</summary>
    public void RecordStackMatch(bool mentionsStackKeyword)
    {
        RaiseOrClear(JobFlag.StackMatch, mentionsStackKeyword);
    }

    /// <summary>Records whether the posting sits in the candidate's home city, raising the flag that says so or clearing it when it no longer does.</summary>
    public void RecordHomeCity(bool isInHomeCity)
    {
        RaiseOrClear(JobFlag.HomeCity, isInHomeCity);
    }

    /// <summary>Raises the high-pay flag when the pay per year in the base currency, the maximum or else the minimum, reaches the threshold, and clears it when the pay falls below it, is unknown, or no threshold is set.</summary>
    public void RefreshHighPayFlag(decimal? thresholdPerYear)
    {
        RaiseOrClear(JobFlag.HighPay, thresholdPerYear is decimal threshold && (CompMaxPerYear ?? CompMinPerYear) >= threshold);
    }

    /// <summary>Replaces what was typed for a job entered by hand: the link and the identity read from it, the company, the title and the location; a job from a source refuses, since the source owns those facts.</summary>
    public void ReviseManualEntry(string fingerprint, string canonicalApplyUrl, string postingUrl, AtsKind? ats, string company, string title, string? locationText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalApplyUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(postingUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(company);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        if (!IsManual)
        {
            throw new InvalidOperationException("Only a job entered by hand can be edited.");
        }

        for (int index = 0; index < Sources.Count; index++)
        {
            if (Sources[index].Kind == JobSourceKind.Manual && Sources[index].SourceId == CanonicalApplyUrl)
            {
                Sources[index] = Sources[index] with { SourceId = canonicalApplyUrl };
            }
        }

        Fingerprint = fingerprint;
        CanonicalApplyUrl = canonicalApplyUrl;
        PostingUrl = postingUrl;
        ApplyUrl = postingUrl;
        Ats = ats;
        Company = company;
        Title = title;
        LocationText = locationText;
    }

    /// <summary>Replaces the description when its hash changed and sends the job back for scoring; returns whether anything changed.</summary>
    public bool ReviseDescription(string descriptionText, string descriptionHash)
    {
        if (string.Equals(DescriptionHash, descriptionHash, StringComparison.Ordinal))
        {
            return false;
        }

        DescriptionText = descriptionText;
        DescriptionHash = descriptionHash;
        Scoring = ScoringState.Unscored;
        ScoreError = null;

        return true;
    }

    /// <summary>Applies a prefilter verdict: the state, the reason when dropped, and the flags raised; the high-pay flag follows the pay the job holds against the given threshold, since the prefilter does not judge pay.</summary>
    public void ApplyPrefilterVerdict(PrefilterState state, string? dropReason, IEnumerable<JobFlag> flags, decimal? highPayThresholdPerYear = null)
    {
        Prefilter = state;
        DropReason = state == PrefilterState.Dropped ? dropReason : null;
        Flags = [.. flags.Distinct()];
        RefreshHighPayFlag(highPayThresholdPerYear);
        Class = state switch
        {
            PrefilterState.Dropped => JobClass.D,
            PrefilterState.Passed => null,
            _ => Class
        };
    }

    /// <summary>Drops a job that passed the prefilter earlier, for instance because it aged out of the inbox.</summary>
    public void Drop(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        Prefilter = PrefilterState.Dropped;
        DropReason = reason;
        Class = JobClass.D;
    }

    /// <summary>Stores a score card together with the class computed from it.</summary>
    public void RecordScore(ScoreCard score, JobClass jobClass)
    {
        ArgumentNullException.ThrowIfNull(score);

        Score = score;
        Class = jobClass;
        Scoring = ScoringState.Scored;
        ScoreError = null;
    }

    /// <summary>Marks scoring as failed with the reason, so that the next score run retries the job.</summary>
    public void FailScoring(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        Scoring = ScoringState.Failed;
        ScoreError = error;
    }

    /// <summary>Counts one full-snapshot run that did not list this job, deactivating it on the second miss; returns whether it just went inactive.</summary>
    public bool MissRun()
    {
        if (!IsActive)
        {
            return false;
        }

        MissedRuns++;
        if (MissedRuns < 2)
        {
            return false;
        }

        IsActive = false;

        return true;
    }

    /// <summary>Marks the job as pursued, which is what creates the application.</summary>
    public void Pursue(DateTimeOffset at)
    {
        Triage = TriageState.Pursued;
        TriagedAt = at;
    }

    /// <summary>Marks the job as skipped so that it leaves the inbox for good.</summary>
    public void Skip(DateTimeOffset at)
    {
        Triage = TriageState.Skipped;
        TriagedAt = at;
    }

    /// <summary>Takes back the triage decision, so that the job waits in the inbox as if it had never been triaged.</summary>
    public void ReturnToInbox()
    {
        Triage = TriageState.New;
        TriagedAt = null;
    }

    private void RaiseOrClear(JobFlag flag, bool isRaised)
    {
        if (!isRaised)
        {
            Flags.Remove(flag);
        }
        else if (!Flags.Contains(flag))
        {
            Flags.Add(flag);
        }
    }
}
