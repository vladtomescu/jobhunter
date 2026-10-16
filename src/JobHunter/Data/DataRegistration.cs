using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JobHunter.Data;

/// <summary>Registers the data folder helper, the database context factory, the database initializer and the daily copy of the database.</summary>
public static class DataRegistration
{
    /// <summary>The configuration key that points the data folder outside the repository (a container mount); read before the content-root fallback.</summary>
    public const string DataRootConfigurationKey = "JobHunter:DataRoot";

    /// <summary>The optional configuration key that keeps the database file in a folder of its own (a container volume) while everything else stays under the data folder.</summary>
    public const string DatabaseFolderConfigurationKey = "JobHunter:DatabaseFolder";

    /// <summary>Registers persistence.</summary>
    public static IServiceCollection AddData(this IServiceCollection services)
    {
        services.AddSingleton(provider =>
        {
            IConfiguration? configuration = provider.GetService<IConfiguration>();
            string? dataRoot = configuration?[DataRootConfigurationKey];
            string? databaseFolder = configuration?[DatabaseFolderConfigurationKey];

            return string.IsNullOrWhiteSpace(dataRoot)
                ? DataPaths.FromContentRoot(provider.GetRequiredService<IHostEnvironment>().ContentRootPath, databaseFolder)
                : new DataPaths(dataRoot, databaseFolder);
        });

        services.AddDbContextFactory<JobHunterDbContext>((provider, options) =>
            options.UseSqlite($"Data Source={provider.GetRequiredService<DataPaths>().DatabaseFile}"));

        services.AddSingleton<DatabaseInitializer>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<DatabaseCopyWriter>();
        services.AddHostedService<DailyDatabaseCopyHostedService>();

        return services;
    }
}
