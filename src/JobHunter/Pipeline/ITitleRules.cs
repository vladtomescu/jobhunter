namespace JobHunter.Pipeline;

/// <summary>How a title fared against the include and exclude rules.</summary>
public enum TitleVerdictKind
{
    Included,
    ExcludedByRule,
    NoIncludeTerm
}

/// <summary>The verdict of the title rules: included, excluded by a named rule, or missing any include term.</summary>
public sealed record TitleVerdict(TitleVerdictKind Kind, string? Reason)
{
    /// <summary>The title carries an include term and trips no exclude rule.</summary>
    public static TitleVerdict Included { get; } = new(TitleVerdictKind.Included, null);

    /// <summary>The title carries no include term at all.</summary>
    public static TitleVerdict NoIncludeTerm { get; } = new(TitleVerdictKind.NoIncludeTerm, null);

    /// <summary>The title trips an exclude rule, named by the reason.</summary>
    public static TitleVerdict ExcludedByRule(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new TitleVerdict(TitleVerdictKind.ExcludedByRule, reason);
    }

    /// <summary>True only when the title is included.</summary>
    public bool Passes => Kind == TitleVerdictKind.Included;
}

/// <summary>The deterministic title rules, applied by the sources before a posting is materialized and by the prefilter afterwards.</summary>
public interface ITitleRules
{
    /// <summary>Judges one job title.</summary>
    TitleVerdict Evaluate(string title);
}
