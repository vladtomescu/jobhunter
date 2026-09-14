using Microsoft.EntityFrameworkCore;

namespace JobHunter.Data;

/// <summary>Registers the data folder helper, the database context factory and the database initializer.</summary>
public static class DataRegistration
{
    /// <summary>Registers persistence.</summary>
    public static IServiceCollection AddData(this IServiceCollection services)
    {
        services.AddSingleton(provider => DataPaths.FromContentRoot(provider.GetRequiredService<IHostEnvironment>().ContentRootPath));

        services.AddDbContextFactory<JobHunterDbContext>((provider, options) =>
            options.UseSqlite($"Data Source={provider.GetRequiredService<DataPaths>().DatabaseFile}"));

        services.AddSingleton<DatabaseInitializer>();

        return services;
    }
}
