using JobHunter.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace JobHunter.Tests.Data;

/// <summary>Proves that AddData honours JobHunter:DataRoot from configuration when it is set, and otherwise resolves the data folder exactly as before the setting existed.</summary>
public sealed class DataRegistrationTests : IDisposable
{
    private readonly string testFolder = Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void AddData_WithDataRootConfigured_PointsAtTheConfiguredFolderInsteadOfTheContentRoot()
    {
        string configuredRoot = Path.Combine(testFolder, "configured-root");
        string contentRoot = Path.Combine(testFolder, "src", "JobHunter");
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [DataRegistration.DataRootConfigurationKey] = configuredRoot })
            .Build();
        ServiceCollection services = new();
        services.AddSingleton(configuration);
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment { ContentRootPath = contentRoot });
        services.AddData();

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Equal(Path.GetFullPath(configuredRoot), provider.GetRequiredService<DataPaths>().Root);
    }

    [Fact]
    public void AddData_WithNoDataRootConfigured_FallsBackToTheContentRootExactlyAsBeforeTheSetting()
    {
        string contentRoot = Path.Combine(testFolder, "src", "JobHunter");
        IConfiguration configuration = new ConfigurationBuilder().Build();
        ServiceCollection services = new();
        services.AddSingleton(configuration);
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment { ContentRootPath = contentRoot });
        services.AddData();

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Equal(DataPaths.FromContentRoot(contentRoot).Root, provider.GetRequiredService<DataPaths>().Root);
    }

    [Fact]
    public void AddData_WithNoConfigurationRegisteredAtAll_FallsBackToTheContentRootJustAsBeforeTheSetting()
    {
        string contentRoot = Path.Combine(testFolder, "src", "JobHunter");
        ServiceCollection services = new();
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment { ContentRootPath = contentRoot });
        services.AddData();

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Equal(DataPaths.FromContentRoot(contentRoot).Root, provider.GetRequiredService<DataPaths>().Root);
    }

    public void Dispose()
    {
        if (Directory.Exists(testFolder))
        {
            Directory.Delete(testFolder, recursive: true);
        }
    }
}
