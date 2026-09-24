namespace JobHunter.Domain;

/// <summary>Applicant tracking system behind a posting, detected from the apply URL or reported by the dataset.</summary>
public enum AtsKind
{
    Greenhouse,
    Lever,
    Ashby,
    Workable,
    SmartRecruiters,
    Other
}

/// <summary>Period a compensation figure is quoted for.</summary>
public enum CompPeriod
{
    Hour,
    Day,
    Month,
    Year
}

/// <summary>Outcome of the deterministic prefilter that runs before any language model call.</summary>
public enum PrefilterState
{
    Pending,
    Passed,
    Dropped
}

/// <summary>Non-blocking marker raised by the prefilter or by scoring; flags never drop a job, they are shown as badges.</summary>
public enum JobFlag
{
    /// <summary>Title is levelled senior or above.</summary>
    H1,

    /// <summary>Employment only, no business-to-business contract offered.</summary>
    H2,

    /// <summary>United States or other non-European working hours.</summary>
    H3,

    /// <summary>Onsite or hybrid, so relocation is implied.</summary>
    H4,

    /// <summary>Compensation is not stated anywhere in the posting.</summary>
    CU,

    /// <summary>The posting requires United States work authorization, which the applicant does not hold.</summary>
    WA,

    /// <summary>The pay in EUR per year reaches <see cref="Job.HighPayThresholdEurYear"/>.</summary>
    HighPay
}

/// <summary>Whether the job carries a usable score.</summary>
public enum ScoringState
{
    Unscored,
    Scored,
    Failed
}

/// <summary>Relevance class computed in code from the score card and the prefilter verdict.</summary>
public enum JobClass
{
    A,
    B,
    C,
    D
}

/// <summary>Where the job stands in the manual triage: untouched, pursued or skipped.</summary>
public enum TriageState
{
    New,
    Pursued,
    Skipped
}
