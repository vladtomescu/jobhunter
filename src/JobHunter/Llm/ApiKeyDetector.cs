namespace JobHunter.Llm;

/// <summary>Reports whether the key that reaches the model is available; it comes from configuration, first the gitignored local settings file and then the environment variable, and never from a tracked file.</summary>
/// <remarks>Reading is virtual so that a test can stand in for an absent or a present key without touching the configuration of the whole test run.</remarks>
public class ApiKeyDetector(IConfiguration configuration)
{
    /// <summary>The gitignored settings file next to appsettings.json that holds the key on this machine; Program.cs loads it with reload on change, so a pasted key is picked up without a restart.</summary>
    public const string LocalSettingsFile = "appsettings.Local.json";

    /// <summary>The configuration key the application reads first.</summary>
    public const string ConfigurationKey = "Anthropic:ApiKey";

    /// <summary>The environment variable read when the configuration key is absent.</summary>
    public const string VariableName = "ANTHROPIC_API_KEY";

    /// <summary>What the scorer and the kit writer report when the key is missing; the interface shows the same sentence next to the export button.</summary>
    public const string MissingKeyMessage = $"No Anthropic key found: put it in {LocalSettingsFile} as {ConfigurationKey} or set the {VariableName} environment variable; until then export the jobs and run the repository skill instead.";

    /// <summary>The key as configuration holds it, or null when neither source has a non-blank value.</summary>
    public virtual string? Read()
    {
        string? value = configuration[ConfigurationKey];

        if (string.IsNullOrWhiteSpace(value))
        {
            value = configuration[VariableName];
        }

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>True when a key is available.</summary>
    public bool IsPresent => Read() is not null;
}
