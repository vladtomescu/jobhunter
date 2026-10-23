using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Pipeline;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Jobs;

/// <summary>Judges the place of every stored job again against the current settings, so a change of home or relocation places reaches jobs already stored, then reclassifies every scored job from its score card; no model is called.</summary>
/// <remarks>Runs at every start and after every settings save. A job the candidate pursued keeps its state and its class, and a job entered by hand is never judged. A job that passed and now lies outside the candidate's places is dropped with the geography reason. A job a geography reason dropped and the rules now keep runs through the whole prefilter as of when it was first seen, so the age rule reads it as it did then, and passes again when nothing else drops it: unscored, it waits for scoring; scored, it is reclassified with the rest.</remarks>
public sealed class LocationRecompute(IDbContextFactory<JobHunterDbContext> contextFactory, Prefilter prefilter, ILogger<LocationRecompute> logger)
{
    /// <summary>Judges every job's place again, reclassifies the scored jobs, saves, and returns how many jobs were dropped or passed again.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        Domain.Settings settings = await context.Settings.SingleAsync(row => row.Id == Domain.Settings.SingletonId, cancellationToken);
        CandidateProfile candidate = CandidateProfile.FromSettings(settings);
        SkillCatalog catalog = new(await context.Skills.AsNoTracking().ToListAsync(cancellationToken));
        List<Job> jobs = await context.Jobs.Where(job => !job.IsManual && job.Triage != TriageState.Pursued).ToListAsync(cancellationToken);
        int dropped = 0;
        int restored = 0;
        int classesChanged = 0;

        foreach (Job job in jobs)
        {
            PrefilterInput input = PrefilterInput.FromJob(job, candidate, settings, job.FirstSeenAt);

            if (job.Prefilter == PrefilterState.Passed && GeographyRules.Evaluate(input) is { Keeps: false, DropReason: string reason })
            {
                job.Drop(reason);
                dropped++;
            }
            else if (job.Prefilter == PrefilterState.Dropped && GeographyRules.IsGeographyDropReason(job.DropReason))
            {
                PrefilterVerdict verdict = prefilter.Evaluate(input);

                if (verdict.State == PrefilterState.Passed)
                {
                    job.PassAgain();
                    restored++;
                }
                else if (verdict.DropReason is string newReason && newReason != job.DropReason)
                {
                    job.Drop(newReason);
                }
            }

            if (CompRecomputeService.Reclassify(job, settings, catalog))
            {
                classesChanged++;
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Places judged again: {Dropped} dropped, {Restored} passed again, {ClassesChanged} classes changed of {Total} jobs.", dropped, restored, classesChanged, jobs.Count);

        return dropped + restored;
    }
}
