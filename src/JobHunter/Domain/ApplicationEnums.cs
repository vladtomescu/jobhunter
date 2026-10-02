namespace JobHunter.Domain;

/// <summary>Stage of an application, with one status per interview round (screening, hiring manager, tech, system design, fit); any transition is allowed and the history rows are the audit trail.</summary>
public enum ApplicationStatus
{
    Saved,
    Applied,
    Screening,
    Manager,
    Tech,
    SystemDesign,
    Fit,
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
