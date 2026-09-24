namespace JobHunter.Sources;

/// <summary>Builds the single User-Agent string every outbound source and pipeline request carries.</summary>
public static class SourceUserAgent
{
    private const string Product = "JobHunter/1.0";

    /// <summary>Builds the User-Agent, appending a contact mailto when the settings email is set, or the bare product string otherwise.</summary>
    public static string Build(string email)
    {
        return string.IsNullOrWhiteSpace(email)
            ? Product
            : $"{Product} (+mailto:{email.Trim()})";
    }
}
