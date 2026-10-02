using JobHunter.Domain;

namespace JobHunter.Tests.Applications;

/// <summary>Covers the ghost-candidate rule: no status change for at least the threshold, and not already at a terminal status; the query never changes anything.</summary>
public sealed class GhostCandidateQueryTests : IAsyncLifetime
{
    private static readonly DateTimeOffset AsOf = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly ApplicationsTestHarness harness = new();

    public Task InitializeAsync()
    {
        return harness.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        return harness.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task FindAsync_OverAMixOfFreshAndStaleApplications_ReturnsOnlyTheStaleNonTerminalOnesOldestFirst()
    {
        Application staleApplied = await SeedApplicationAsync(ApplicationStatus.Applied, AsOf.AddDays(-30));
        await SeedApplicationAsync(ApplicationStatus.Screening, AsOf.AddDays(-10));
        await SeedApplicationAsync(ApplicationStatus.Rejected, AsOf.AddDays(-30));
        Application staleAtThreshold = await SeedApplicationAsync(ApplicationStatus.Tech, AsOf.AddDays(-21));
        await SeedApplicationAsync(ApplicationStatus.Saved, AsOf.AddDays(-20));

        IReadOnlyList<Application> ghosts = await harness.GhostCandidates.FindAsync(21, AsOf);

        Assert.Equal<Guid>([staleApplied.Id, staleAtThreshold.Id], [.. ghosts.Select(application => application.Id)]);
    }

    [Fact]
    public async Task FindAsync_WhenEveryStaleApplicationIsTerminal_ReturnsNothing()
    {
        await SeedApplicationAsync(ApplicationStatus.Accepted, AsOf.AddDays(-60));
        await SeedApplicationAsync(ApplicationStatus.Rejected, AsOf.AddDays(-60));
        await SeedApplicationAsync(ApplicationStatus.Withdrawn, AsOf.AddDays(-60));
        await SeedApplicationAsync(ApplicationStatus.Ghosted, AsOf.AddDays(-60));

        IReadOnlyList<Application> ghosts = await harness.GhostCandidates.FindAsync(21, AsOf);

        Assert.Empty(ghosts);
    }

    private async Task<Application> SeedApplicationAsync(ApplicationStatus status, DateTimeOffset statusChangedAt)
    {
        Application application = Application.Create(Guid.CreateVersion7(), status, statusChangedAt, "seeded");
        await harness.SaveAsync(application);

        return application;
    }
}
