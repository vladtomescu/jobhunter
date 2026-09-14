namespace JobHunter.Domain;

/// <summary>What started a refresh run.</summary>
public enum FetchTrigger
{
    Manual,
    Startup
}

/// <summary>Where a refresh run ended up.</summary>
public enum FetchOutcome
{
    Running,
    Completed,
    Failed
}
