using Microsoft.EntityFrameworkCore;

namespace JobHunter.Data;

/// <summary>Registers the data folder helper, the database context factory and the database initializer.</summary>
public static class DataRegistration
{
    /// <summary>The configuration key that points the data folder outside the repository (a container mount); read before the content-root fallback.</summary>
    public const string DataRootConfigurationKey = "JobHunter:DataRoot";

    /// <summary>Registers persistence.</summary>
    public static IServiceCollection AddData(this IServiceCollection services)
    {
        services.AddSingleton(provider =>
        {
            string? dataRoot = provider.GetService<IConfiguration>()?[DataRootConfigurationKey];

            return string.IsNullOrWhiteSpace(dataRoot)
                ? DataPaths.FromContentRoot(provider.GetRequiredService<IHostEnvironment>().ContentRootPath)
                : new DataPaths(dataRoot);
        });

        services.AddDbContextFactory<JobHunterDbContext>((provider, options) =>
            options.UseSqlite($"Data Source={provider.GetRequiredService<DataPaths>().DatabaseFile}"));

        services.AddSingleton<DatabaseInitializer>();

        return services;
    }
}
