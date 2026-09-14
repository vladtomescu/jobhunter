namespace JobHunter.Tests;

/// <summary>A fact that only runs when the environment variable JOBHUNTER_LIVE_TESTS is 1, because it reaches the network or a paid API.</summary>
public sealed class LiveFactAttribute : FactAttribute
{
    /// <summary>The environment variable that turns live tests on.</summary>
    public const string SwitchName = "JOBHUNTER_LIVE_TESTS";

    /// <summary>Skips the test unless live tests are switched on.</summary>
    public LiveFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(SwitchName), "1", StringComparison.Ordinal))
        {
            Skip = $"Set {SwitchName}=1 to run tests that reach the network or a paid API.";
        }
    }
}
