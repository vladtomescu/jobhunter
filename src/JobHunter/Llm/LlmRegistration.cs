using JobHunter.Data;
using JobHunter.Llm.Exchange;

namespace JobHunter.Llm;

/// <summary>Registers the scorer, the kit writer, the prompt catalog and the exchange files of the backup path.</summary>
public static class LlmRegistration
{
    /// <summary>Registers the language model services.</summary>
    public static IServiceCollection AddLlm(this IServiceCollection services)
    {
        services.AddSingleton<ApiKeyDetector>();
        services.AddSingleton<AnthropicClientFactory>();
        services.AddSingleton(provider => PromptCatalog.ForDataFolder(provider.GetRequiredService<DataPaths>()));
        services.AddSingleton<IJobScorer, AnthropicJobScorer>();
        services.AddSingleton<IKitWriter, AnthropicKitWriter>();
        services.AddSingleton<ExchangeExporter>();
        services.AddSingleton<ExchangeImporter>();

        return services;
    }
}
