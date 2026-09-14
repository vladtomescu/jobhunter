using JobHunter.Data;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Settings;

/// <summary>Reads and changes the single settings row; every call works on its own context, so callers always see the current values.</summary>
public sealed class SettingsService(IDbContextFactory<JobHunterDbContext> contextFactory)
{
    /// <summary>Returns a fresh detached snapshot of the settings, seeding the row with the defaults the first time it is asked for.</summary>
    public async Task<Domain.Settings> GetAsync(CancellationToken cancellationToken = default)
    {
        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Domain.Settings? settings = await context.Settings
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == Domain.Settings.SingletonId, cancellationToken);

        return settings ?? await SeedAsync(context, cancellationToken);
    }

    /// <summary>Applies a change to the settings row and saves it, returning the saved values.</summary>
    public async Task<Domain.Settings> ApplyAsync(Action<Domain.Settings> change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Domain.Settings settings = await context.Settings
            .FirstOrDefaultAsync(row => row.Id == Domain.Settings.SingletonId, cancellationToken)
            ?? await SeedAsync(context, cancellationToken);

        change(settings);
        await context.SaveChangesAsync(cancellationToken);

        return settings;
    }

    private static async Task<Domain.Settings> SeedAsync(JobHunterDbContext context, CancellationToken cancellationToken)
    {
        Domain.Settings settings = Domain.Settings.CreateDefault();
        context.Settings.Add(settings);
        await context.SaveChangesAsync(cancellationToken);

        return settings;
    }
}
