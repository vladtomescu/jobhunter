using JobHunter.Data;
using JobHunter.Llm.Exchange;

namespace JobHunter.Llm;

/// <summary>Registers the scorer, the kit writer, the prompt catalog with its startup report of the profile sources, and the exchange files of the backup path.</summary>
public static class LlmRegistration
{
    /// <summary>The configuration key that points the repository root outside the deployed data folder (a container mount); read before the data-folder fallback. The profile files are read from the data root either way.</summary>
    public const string RepositoryRootConfigurationKey = "JobHunter:RepositoryRoot";

    /// <summary>Registers the language model services.</summary>
    public static IServiceCollection AddLlm(this IServiceCollection services)
    {
        services.AddSingleton<ApiKeyDetector>();
        services.AddSingleton<AnthropicClientFactory>();
        services.AddSingleton(provider =>
        {
            DataPaths dataPaths = provider.GetRequiredService<DataPaths>();
            string? repositoryRoot = provider.GetService<IConfiguration>()?[RepositoryRootConfigurationKey];

            return string.IsNullOrWhiteSpace(repositoryRoot)
                ? PromptCatalog.ForDataFolder(dataPaths)
                : PromptCatalog.ForDataFolder(dataPaths, repositoryRoot);
        });
        services.AddSingleton<IJobScorer, AnthropicJobScorer>();
        services.AddSingleton<IKitWriter, AnthropicKitWriter>();
        services.AddSingleton<ExchangeExporter>();
        services.AddSingleton<ExchangeImporter>();
        services.AddSingleton<NewJobImporter>();
        services.AddHostedService<ProfileSourceReport>();

        return services;
    }
}
