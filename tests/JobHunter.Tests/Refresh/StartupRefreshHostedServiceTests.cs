using JobHunter.Domain;
using JobHunter.Refresh;
using JobHunter.Sources;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobHunter.Tests.Refresh;

/// <summary>A lifetime whose application never reports that it started, so the hosted service waits instead of running a refresh.</summary>
internal sealed class NeverStartingApplicationLifetime : IHostApplicationLifetime
{
    /// <inheritdoc />
    public CancellationToken ApplicationStarted => CancellationToken.None;

    /// <inheritdoc />
    public CancellationToken ApplicationStopping => CancellationToken.None;

    /// <inheritdoc />
    public CancellationToken ApplicationStopped => CancellationToken.None;

    /// <inheritdoc />
    public void StopApplication()
    {
    }
}

/// <summary>Proves that the refresh that runs at startup first puts the run stored by an earlier session on the panel.</summary>
public sealed class StartupRefreshHostedServiceTests
{
    [Fact]
    public async Task StartAsync_WithAFinishedRunFromAnEarlierSession_ShowsThatRunOnThePanelState()
    {
        await using RefreshTestHarness harness = new();
        await harness.InitializeAsync();
        DateTimeOffset at = DateTimeOffset.UtcNow.AddHours(-1);
        FetchRun stored = FetchRun.Start(FetchTrigger.Manual, at);
        stored.RecordSourceResult(JobSourceKind.Dataset, 40, 6, 2, 3, null);
        stored.RecordScoring(5, 0);
        stored.Complete(at.AddMinutes(2));
        await harness.SaveAsync(stored);

        using StartupRefreshHostedService service = new(new NeverStartingApplicationLifetime(), harness.Refresher, NullLogger<StartupRefreshHostedService>.Instance);
        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        RefreshRunSummary? summary = harness.State.LastRun;
        Assert.NotNull(summary);
        Assert.Equal(stored.Id, summary.RunId);
        Assert.Equal(5, summary.Scored);
        Assert.Equal(6, summary.NewJobs);
    }
}
