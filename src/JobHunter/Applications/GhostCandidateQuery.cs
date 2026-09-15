using JobHunter.Data;
using JobHunter.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Applications;

/// <summary>Finds applications that went quiet: no status change for at least the threshold, and not already at a terminal status. A result only ever surfaces the candidates; nothing here changes a status.</summary>
public sealed class GhostCandidateQuery(IDbContextFactory<JobHunterDbContext> contextFactory)
{
    private static readonly IReadOnlyCollection<ApplicationStatus> TerminalStatuses =
    [
        ApplicationStatus.Accepted,
        ApplicationStatus.Rejected,
        ApplicationStatus.Withdrawn,
        ApplicationStatus.Ghosted
    ];

    /// <summary>Returns the applications, oldest first, whose status has not changed in at least thresholdDays days as of asOf and which have not reached a terminal status.</summary>
    public async Task<IReadOnlyList<Application>> FindAsync(int thresholdDays, DateTimeOffset asOf, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(thresholdDays);

        DateTimeOffset cutoff = asOf.AddDays(-thresholdDays);

        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Applications
            .AsNoTracking()
            .Where(application => application.StatusChangedAt <= cutoff && !TerminalStatuses.Contains(application.Status))
            .OrderBy(application => application.StatusChangedAt)
            .ToListAsync(cancellationToken);
    }
}
