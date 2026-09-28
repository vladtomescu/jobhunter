using JobHunter.Data;
using JobHunter.Llm;
using JobHunter.Llm.Exchange;
using JobHunter.Pipeline;
using JobHunter.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace JobHunter.Tests.Llm;

/// <summary>Proves that one call to AddLlm gives the rest of the application a scorer, a kit writer and both ends of the exchange.</summary>
public sealed class LlmRegistrationTests
{
    [Fact]
    public void AddLlm_OnAnEmptyCollection_BindsTheScorerAndTheKitWriterToTheModel()
    {
        using ServiceProvider provider = BuildProvider();

        Assert.IsType<AnthropicJobScorer>(provider.GetRequiredService<IJobScorer>());
        Assert.IsType<AnthropicKitWriter>(provider.GetRequiredService<IKitWriter>());
    }

    [Fact]
    public void AddLlm_OnAnEmptyCollection_BindsTheCoverLetterWriterToTheModel()
    {
        using ServiceProvider provider = BuildProvider();

        Assert.IsType<AnthropicCoverLetterWriter>(provider.GetRequiredService<ICoverLetterWriter>());
    }

    [Fact]
    public void AddLlm_OnAnEmptyCollection_RegistersThePromptCatalogAndBothEndsOfTheExchange()
    {
        using ServiceProvider provider = BuildProvider();

        Assert.NotNull(provider.GetRequiredService<PromptCatalog>());
        Assert.NotNull(provider.GetRequiredService<ApiKeyDetector>());
        Assert.NotNull(provider.GetRequiredService<AnthropicClientFactory>());
        Assert.NotNull(provider.GetRequiredService<ExchangeExporter>());
        Assert.NotNull(provider.GetRequiredService<ExchangeImporter>());
    }

    [Fact]
    public void AddLlm_ResolvedTwice_ReturnsTheSameScorer()
    {
        using ServiceProvider provider = BuildProvider();

        Assert.Same(provider.GetRequiredService<IJobScorer>(), provider.GetRequiredService<IJobScorer>());
    }

    [Fact]
    public void AddLlm_InARepositoryWithPrompts_FindsThePromptFilesFromTheDataFolder()
    {
        ServiceCollection services = new();
        services.AddData();
        services.AddSettings();
        services.AddPipeline();
        services.AddLlm();
        services.AddSingleton(new DataPaths(Path.Combine(LlmFixtures.RepositoryRoot(), "data")));

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Contains("# Rubric", provider.GetRequiredService<PromptCatalog>().ScoringSystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void AddLlm_WithRepositoryRootConfigured_ReadsThePromptsFromItInsteadOfTheDataFolder()
    {
        ServiceCollection services = new();
        services.AddData();
        services.AddSettings();
        services.AddPipeline();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [LlmRegistration.RepositoryRootConfigurationKey] = LlmFixtures.RepositoryRoot() })
            .Build());
        services.AddLlm();
        services.AddSingleton(new DataPaths(Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"))));

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Contains("# Rubric", provider.GetRequiredService<PromptCatalog>().ScoringSystemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void AddLlm_WithRepositoryRootConfigured_StillReadsTheUserProfileFromTheDataRoot()
    {
        string dataRoot = Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"));
        string userFolder = Path.Combine(dataRoot, PromptCatalog.ProfileFolderName);
        Directory.CreateDirectory(userFolder);
        File.WriteAllText(Path.Combine(userFolder, PromptCatalog.ProfileFileName), "# Profile\n\nData-root profile marker.");
        ServiceCollection services = new();
        services.AddData();
        services.AddSettings();
        services.AddPipeline();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [LlmRegistration.RepositoryRootConfigurationKey] = LlmFixtures.RepositoryRoot() })
            .Build());
        services.AddLlm();
        services.AddSingleton(new DataPaths(dataRoot));

        try
        {
            using ServiceProvider provider = services.BuildServiceProvider();
            PromptCatalog catalog = provider.GetRequiredService<PromptCatalog>();

            Assert.Equal(ProfileFileOrigin.DataRoot, catalog.ProfileSources[0].Origin);
            Assert.Equal(ProfileFileOrigin.Example, catalog.ProfileSources[1].Origin);
            Assert.Contains("Data-root profile marker.", catalog.ScoringSystemPrompt, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    [Fact]
    public void AddLlm_OnAnEmptyCollection_RegistersTheProfileSourceReportToRunAtStartup()
    {
        ServiceCollection services = new();
        services.AddLlm();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(ProfileSourceReport));
    }

    [Fact]
    public void AddLlm_WithNoRepositoryRootConfigured_StillFindsThePromptFilesFromTheDataFolderJustAsBeforeTheSetting()
    {
        ServiceCollection services = new();
        services.AddData();
        services.AddSettings();
        services.AddPipeline();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLlm();
        services.AddSingleton(new DataPaths(Path.Combine(LlmFixtures.RepositoryRoot(), "data")));

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Contains("# Rubric", provider.GetRequiredService<PromptCatalog>().ScoringSystemPrompt, StringComparison.Ordinal);
    }

    private static ServiceProvider BuildProvider()
    {
        ServiceCollection services = new();
        services.AddData();
        services.AddSettings();
        services.AddPipeline();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLlm();
        services.AddSingleton(new DataPaths(Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"))));

        return services.BuildServiceProvider();
    }
}
