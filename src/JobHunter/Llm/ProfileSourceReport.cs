namespace JobHunter.Llm;

/// <summary>Logs once at startup where each profile file is read from, so a profile that falls back to the shipped example is visible.</summary>
public sealed class ProfileSourceReport(PromptCatalog prompts, ILogger<ProfileSourceReport> logger) : IHostedService
{
    /// <summary>Writes one line per profile file: the user's copy in the data root, the shipped example, or neither.</summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (ProfileFileSource source in prompts.ProfileSources)
        {
            switch (source.Origin)
            {
                case ProfileFileOrigin.DataRoot:
                    logger.LogInformation("Profile file {FileName} is read from the data root at {Path}.", source.FileName, source.UserPath);
                    break;
                case ProfileFileOrigin.Example:
                    logger.LogInformation("Profile file {FileName} is read from the shipped example at {Path}, because {UserPath} does not exist.", source.FileName, source.ExamplePath, source.UserPath);
                    break;
                default:
                    logger.LogWarning("Profile file {FileName} is missing: neither {UserPath} nor the example {ExamplePath} exists, so every model call that reads it will fail.", source.FileName, source.UserPath, source.ExamplePath);
                    break;
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>Nothing runs in the background, so there is nothing to stop.</summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
