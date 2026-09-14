using JobHunter.Settings;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Data;

/// <summary>Brings the database up to the current migration and makes sure the settings row exists; runs once at startup and in tests.</summary>
public sealed class DatabaseInitializer(IDbContextFactory<JobHunterDbContext> contextFactory, SettingsService settingsService)
{
    /// <summary>Applies pending migrations and seeds the settings row.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);

        await settingsService.GetAsync(cancellationToken);
    }
}
