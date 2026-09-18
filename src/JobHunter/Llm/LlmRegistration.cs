using JobHunter.Data;
using JobHunter.Llm.Exchange;

namespace JobHunter.Llm;

/// <summary>Registers the scorer, the kit writer, the prompt catalog and the exchange files of the backup path.</summary>
public static class LlmRegistration
{
    /// <summary>The configuration key that points the repository root outside the deployed data folder (a container mount); read before the data-folder fallback.</summary>
    public const string RepositoryRootConfigurationKey = "JobHunter:RepositoryRoot";

    /// <summary>Registers the language model services.</summary>
    public static IServiceCollection AddLlm(this IServiceCollection services)
    {
        services.AddSingleton<ApiKeyDetector>();
        services.AddSingleton<AnthropicClientFactory>();
        services.AddSingleton(provider =>
        {
            string? repositoryRoot = provider.GetService<IConfiguration>()?[RepositoryRootConfigurationKey];

            return string.IsNullOrWhiteSpace(repositoryRoot)
                ? PromptCatalog.ForDataFolder(provider.GetRequiredService<DataPaths>())
                : new PromptCatalog(repositoryRoot);
        });
        services.AddSingleton<IJobScorer, AnthropicJobScorer>();
        services.AddSingleton<IKitWriter, AnthropicKitWriter>();
        services.AddSingleton<ExchangeExporter>();
        services.AddSingleton<ExchangeImporter>();

        return services;
    }
}
