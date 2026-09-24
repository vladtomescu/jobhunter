namespace JobHunter.Llm;

/// <summary>Which copy of a profile file the prompt catalog reads.</summary>
public enum ProfileFileOrigin
{
    /// <summary>The user's own file in the profile folder of the data root.</summary>
    DataRoot,

    /// <summary>The example shipped in the repository's profile folder, read because the user has no copy of their own.</summary>
    Example,

    /// <summary>Neither copy exists, so a prompt that needs the file cannot be composed.</summary>
    Missing
}
