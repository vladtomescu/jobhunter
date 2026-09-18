using JobHunter.Data;
using JobHunter.Llm;
using JobHunter.Llm.Exchange;
using JobHunter.Pipeline;
using JobHunter.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
