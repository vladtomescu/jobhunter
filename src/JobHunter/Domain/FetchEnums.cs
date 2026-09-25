namespace JobHunter.Domain;

/// <summary>What started a run: a refresh from the button or at startup, or a score run from the Score button.</summary>
public enum FetchTrigger
{
    Manual,
    Startup,
    Score
}

/// <summary>Where a refresh run ended up.</summary>
public enum FetchOutcome
{
    Running,
    Completed,
    Failed
}
