using JobHunter.Data;
using JobHunter.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Tests.Settings;

/// <summary>Wires the settings service over a temp SQLite database, following the temp-DB pattern the data tests use.</summary>
internal sealed class SettingsEndpointsTestHarness : IAsyncDisposable
{
    private readonly string dataFolder = Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider provider;

    public SettingsEndpointsTestHarness()
    {
        ServiceCollection services = new();
        services.AddData();
        services.AddSettings();
        services.AddSingleton(new DataPaths(dataFolder));

        provider = services.BuildServiceProvider();
    }

    public SettingsService Settings => provider.GetRequiredService<SettingsService>();

    public async Task InitializeAsync()
    {
        await provider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await provider.DisposeAsync();
        TestDataFolder.Delete(dataFolder);
    }
}
