namespace JobHunter.Components.Shared;

/// <summary>Whether the page of one circuit is loading, which the bar at the top of the window shows: while a link leads to another page and while a page reads its data again.</summary>
public sealed class PageLoading
{
    private int runningLoads;
    private bool navigating;

    /// <summary>Raised after the bar turns on or off.</summary>
    public event Action? Changed;

    /// <summary>True while a page is on its way or any load runs.</summary>
    public bool IsLoading => navigating || runningLoads > 0;

    /// <summary>Runs one load with the bar on and hands back what it read.</summary>
    public async Task<T> RunAsync<T>(Func<Task<T>> load)
    {
        ArgumentNullException.ThrowIfNull(load);

        runningLoads++;
        Changed?.Invoke();

        try
        {
            // SQLite runs its async calls synchronously, so the load would end before the bar is drawn without this yield.
            await Task.Yield();

            return await load();
        }
        finally
        {
            runningLoads--;
            Changed?.Invoke();
        }
    }

    /// <summary>Turns the bar on when a link leads to another page; the layout turns it off once the new page is handed to it.</summary>
    public async ValueTask StartNavigationAsync()
    {
        navigating = true;
        Changed?.Invoke();

        // The new page is built right after this returns, so yield to let the bar reach the browser first.
        await Task.Yield();
    }

    /// <summary>Turns the bar off once the new page is in place.</summary>
    public void EndNavigation()
    {
        if (!navigating)
        {
            return;
        }

        navigating = false;
        Changed?.Invoke();
    }
}
