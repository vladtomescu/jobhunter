using JobHunter.Data;
using JobHunter.Domain;

namespace JobHunter.Refresh;

/// <summary>Closes the record of a refresh or a score run that threw, with the error that stopped it.</summary>
internal static class FailedRunRecorder
{
    /// <summary>Marks the run failed and saves it; a save that fails too is logged, so the original failure is never hidden by a second one.</summary>
    public static async Task RecordAsync(JobHunterDbContext context, FetchRun run, Exception exception, ILogger logger)
    {
        string message = string.IsNullOrWhiteSpace(exception.Message) ? exception.GetType().Name : exception.Message;
        run.Fail(message, DateTimeOffset.UtcNow);

        try
        {
            await context.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception saveFailure)
        {
            logger.LogError(saveFailure, "The failed {Trigger} run could not be stored.", run.Trigger);
        }
    }
}
