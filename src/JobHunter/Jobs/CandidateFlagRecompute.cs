using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Pipeline;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Jobs;

/// <summary>Recomputes the settings-driven flags of every stored job from the current settings, touching no other flag and calling no model.</summary>
/// <remarks>Runs at every start and after every settings save, so a candidate profile change (stack keywords, home city, high-pay threshold) reaches stored jobs without a restart. It stays deliberately narrow: <see cref="JobFlag.H3"/>, <see cref="JobFlag.H4"/> and drop decisions depend on the remote policy the prefilter reads from the raw posting, so they are not recomputed here and only follow from the next prefilter pass. <see cref="JobFlag.HomeCity"/> is read the same way: the prefilter only ever raises it inside the branch that also raises H4, so a remote job whose free-text location list happens to name the home city (a board listing every hub it hires from, say) must not be flagged; the stored H4 flag is the cheap, already-correct stand-in for "onsite or hybrid" here.</remarks>
public sealed class CandidateFlagRecompute(IDbContextFactory<JobHunterDbContext> contextFactory, ILogger<CandidateFlagRecompute> logger)
{
    /// <summary>Recomputes the stack-match, home-city and high-pay flags on every job, saves the jobs whose flags changed and returns how many did.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        Domain.Settings settings = await context.Settings.SingleAsync(row => row.Id == Domain.Settings.SingletonId, cancellationToken);
        CandidateProfile candidate = CandidateProfile.FromSettings(settings);
        List<Job> jobs = await context.Jobs.ToListAsync(cancellationToken);
        int changed = 0;

        foreach (Job job in jobs)
        {
            JobFlag[] before = [.. job.Flags];

            job.RecordStackMatch(candidate.StackKeywords.Matches(job.Title, job.Tags, job.DescriptionText));
            job.RecordHomeCity(job.Flags.Contains(JobFlag.H4) && GeographyRules.IsInHomeCity(candidate.HomeCity, PlaceText(job)));
            job.RefreshHighPayFlag(settings.HighPayThresholdPerYear);

            if (!before.SequenceEqual(job.Flags))
            {
                changed++;
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Candidate flags recomputed: {Changed} of {Total} jobs changed.", changed, jobs.Count);

        return changed;
    }

    private static string PlaceText(Job job)
    {
        return string.Join(' ', new[] { job.LocationText, job.RegionText }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }
}
