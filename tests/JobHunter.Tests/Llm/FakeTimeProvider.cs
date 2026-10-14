namespace JobHunter.Tests.Llm;

/// <summary>A clock the test moves by hand, so a claim's lease can run out without waiting for it.</summary>
internal sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset now = start;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        return now;
    }

    /// <summary>Moves the clock forward.</summary>
    public void Advance(TimeSpan elapsed)
    {
        now += elapsed;
    }
}
