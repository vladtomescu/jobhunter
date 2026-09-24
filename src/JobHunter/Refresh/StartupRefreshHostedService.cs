using JobHunter.Domain;

namespace JobHunter.Refresh;

/// <summary>Puts the run that finished last on the panel while the application starts, then runs one refresh, but only when that run is older than the automatic refresh interval in the settings.</summary>
public sealed class StartupRefreshHostedService(IHostApplicationLifetime lifetime, RefreshService refreshService, ILogger<StartupRefreshHostedService> logger) : BackgroundService
{
    /// <summary>Reads the last finished run onto the shared state before the first page renders, then starts the background loop.</summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await refreshService.RestoreLastRunAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The summary of the last refresh could not be read.");
        }

        await base.StartAsync(cancellationToken);
    }

    /// <summary>Waits for the application to finish starting, then refreshes when one is due.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await WaitForApplicationStartAsync(stoppingToken))
        {
            return;
        }

        try
        {
            if (!await refreshService.IsStartupRefreshDueAsync(stoppingToken))
            {
                logger.LogInformation("No refresh runs at startup: the last one is recent enough, or the startup refresh is turned off.");

                return;
            }

            RefreshResult result = await refreshService.RunAsync(FetchTrigger.Startup, stoppingToken);

            if (result.Refusal is string refusal)
            {
                logger.LogInformation("The startup refresh was not needed: {Refusal}", refusal);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("The startup refresh was cancelled because the application is shutting down.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The startup refresh failed.");
        }
    }

    private async Task<bool> WaitForApplicationStartAsync(CancellationToken stoppingToken)
    {
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        await using CancellationTokenRegistration onStarted = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await using CancellationTokenRegistration onStopping = stoppingToken.Register(() => started.TrySetCanceled(stoppingToken));

        try
        {
            await started.Task;

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
