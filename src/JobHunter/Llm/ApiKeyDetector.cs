namespace JobHunter.Llm;

/// <summary>Reports whether the key that reaches the model is available; it is read from the environment only, never from configuration and never from a file.</summary>
/// <remarks>Reading is virtual so that a test can stand in for an absent or a present key without touching the environment of the whole test run.</remarks>
public class ApiKeyDetector
{
    /// <summary>The one environment variable the key is read from.</summary>
    public const string VariableName = "ANTHROPIC_API_KEY";

    /// <summary>What the scorer and the kit writer report when the key is missing; the interface shows the same sentence next to the export button.</summary>
    public const string MissingKeyMessage = $"{VariableName} is not set, so the model cannot be reached from the application; export the jobs and run the repository skill instead.";

    /// <summary>The key as the environment holds it, or null when the variable is unset or blank.</summary>
    public virtual string? Read()
    {
        string? value = Environment.GetEnvironmentVariable(VariableName);

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>True when a key is available.</summary>
    public bool IsPresent => Read() is not null;
}
