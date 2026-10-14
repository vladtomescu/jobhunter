using JobHunter.Domain;

namespace JobHunter.Refresh;

/// <summary>The one order jobs are scored in, by the Score button and on the subscription path alike: the jobs added by hand, then the jobs that need no relocation, then the ones that do, each of the last two by stated yearly pay highest first with unknown pay last, and ties to the newest.</summary>
/// <remarks>A job needs relocation when it is onsite or hybrid (<see cref="JobFlag.H4"/>) outside the home city (<see cref="JobFlag.HomeCity"/>). The whole order runs in SQL: the flags are a primitive collection whose Contains translates, and the pay and the dates sit in columns SQLite can sort.</remarks>
public static class ScoringOrder
{
    /// <summary>Puts the jobs in scoring order.</summary>
    public static IOrderedQueryable<Job> Apply(IQueryable<Job> jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);

        return jobs
            .OrderBy(job => job.IsManual ? 0 : job.Flags.Contains(JobFlag.H4) && !job.Flags.Contains(JobFlag.HomeCity) ? 2 : 1)
            .ThenBy(job => !job.IsManual && (job.CompMaxPerYear ?? job.CompMinPerYear) == null ? 1 : 0)
            .ThenByDescending(job => job.IsManual ? null : job.CompMaxPerYear ?? job.CompMinPerYear)
            .ThenByDescending(job => job.PostedAt ?? job.FirstSeenAt);
    }
}
