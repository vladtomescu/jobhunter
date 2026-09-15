using JobHunter.Applications;
using JobHunter.Data;
using JobHunter.Llm;
using JobHunter.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Tests.Applications;

/// <summary>Proves AddApplications() registers every service the module exposes.</summary>
public sealed class ApplicationsRegistrationTests
{
    [Fact]
    public void AddApplications_OnAServiceCollection_RegistersTriageApplicationGhostAndStatsServices()
    {
        ServiceCollection services = new();
        services.AddData();
        services.AddSettings();
        services.AddSingleton<IKitWriter>(new FakeKitWriter());
        services.AddApplications();
        services.AddSingleton(new DataPaths(Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"))));

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<TriageService>());
        Assert.NotNull(provider.GetRequiredService<ApplicationService>());
        Assert.NotNull(provider.GetRequiredService<GhostCandidateQuery>());
        Assert.NotNull(provider.GetRequiredService<StatsService>());
    }
}
