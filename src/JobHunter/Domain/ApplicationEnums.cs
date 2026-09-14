namespace JobHunter.Domain;

/// <summary>Stage of an application; any transition is allowed and the history rows are the audit trail.</summary>
public enum ApplicationStatus
{
    Saved,
    Applied,
    Screening,
    Interview1,
    Interview2,
    Final,
    Offer,
    Accepted,
    Rejected,
    Withdrawn,
    Ghosted
}

/// <summary>Route the application was submitted through.</summary>
public enum ApplicationChannel
{
    Ats,
    LinkedIn,
    Email,
    Referral,
    Other
}

/// <summary>Progress of the application kit generation.</summary>
public enum KitState
{
    None,
    Generating,
    Ready,
    Failed
}
