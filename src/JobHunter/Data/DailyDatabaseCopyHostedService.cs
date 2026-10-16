namespace JobHunter.Data;

/// <summary>Writes today's copy of the database when the application starts and looks again every hour, so an application that stays up for days still gets one copy per day.</summary>
public sealed class DailyDatabaseCopyHostedService(DatabaseCopyWriter copyWriter, TimeProvider timeProvider, ILogger<DailyDatabaseCopyHostedService> logger) : BackgroundService
{
    /// <summary>How long the service waits between two looks for today's copy.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    /// <summary>Checks at once, then on every tick until the application stops.</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(CheckInterval, timeProvider);

        do
        {
            await CheckAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Writes today's copy when it is missing; a failure is logged as a warning and never stops the application, and the next check tries again.</summary>
    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            string? copyFile = await copyWriter.WriteTodaysCopyAsync(cancellationToken);

            if (copyFile is not null)
            {
                logger.LogInformation("Wrote today's copy of the database to {CopyFile}.", copyFile);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "The daily copy of the database failed; the next check tries again.");
        }
    }
}
