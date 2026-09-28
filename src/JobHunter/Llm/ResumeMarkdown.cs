namespace JobHunter.Llm;

/// <summary>Reads the resume markdown that kits and cover letters may draw facts from.</summary>
public static class ResumeMarkdown
{
    /// <summary>The resume at the saved path, or null when no path is saved or the file does not exist or cannot be read.</summary>
    public static async Task<string?> ReadAsync(string? resumeMarkdownPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resumeMarkdownPath) || !File.Exists(resumeMarkdownPath))
        {
            return null;
        }

        try
        {
            return await File.ReadAllTextAsync(resumeMarkdownPath, cancellationToken);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
