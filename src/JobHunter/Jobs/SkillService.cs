using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Pipeline;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Jobs;

/// <summary>Changes the skill catalog by hand and keeps the class of every job that names a changed skill in step, from the stored score cards and without calling any model.</summary>
/// <remarks>Every change runs under the gate scoring records its skills under, so a score in flight never saves the model's mark over the candidate's and a merge never interleaves with a score naming the skill it deletes.</remarks>
public sealed class SkillService(IDbContextFactory<JobHunterDbContext> contextFactory, SkillCatalogGate skillGate)
{
    /// <summary>Puts the candidate's CV mark on a skill, which the model never overwrites, reclassifies every scored job whose card names the skill under any of its names, and returns how many of them changed class.</summary>
    public Task<int> SetCvMarkAsync(int skillId, CvMark cvMark, CancellationToken cancellationToken = default)
    {
        return skillGate.RunAsync(
            async () =>
            {
                await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

                List<Skill> skills = await context.Skills.ToListAsync(cancellationToken);
                Skill skill = skills.Single(candidate => candidate.Id == skillId);
                skill.MarkByCandidate(cvMark);

                return await ReclassifyAndSaveAsync(context, new SkillCatalog(skills), skill, cancellationToken);
            },
            cancellationToken);
    }

    /// <summary>Merges one skill into another: the target takes every name of the merged skill as an alias and keeps its own category and CV mark, the merged skill is deleted, and every scored job whose card now resolves to the target is reclassified; returns how many of them changed class.</summary>
    /// <remarks>No score card is rewritten: the cards keep the names the model wrote, which now resolve to the target through its aliases.</remarks>
    public Task<int> MergeAsync(int mergedSkillId, int targetSkillId, CancellationToken cancellationToken = default)
    {
        if (mergedSkillId == targetSkillId)
        {
            throw new ArgumentException("A skill cannot be merged into itself.", nameof(targetSkillId));
        }

        return skillGate.RunAsync(
            async () =>
            {
                await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

                List<Skill> skills = await context.Skills.ToListAsync(cancellationToken);
                Skill merged = skills.Single(candidate => candidate.Id == mergedSkillId);
                Skill target = skills.Single(candidate => candidate.Id == targetSkillId);

                target.TakeNamesOf(merged);
                skills.Remove(merged);
                context.Skills.Remove(merged);

                return await ReclassifyAndSaveAsync(context, new SkillCatalog(skills), target, cancellationToken);
            },
            cancellationToken);
    }

    /// <summary>Reclassifies every scored job whose card names the changed skill under any of its names in the catalog as changed, saves the catalog and the jobs in one save, and returns how many jobs changed class.</summary>
    private static async Task<int> ReclassifyAndSaveAsync(JobHunterDbContext context, SkillCatalog catalog, Skill changed, CancellationToken cancellationToken)
    {
        Domain.Settings settings = await context.Settings.SingleAsync(row => row.Id == Domain.Settings.SingletonId, cancellationToken);
        List<Job> scoredJobs = await context.Jobs.Where(job => job.Scoring == ScoringState.Scored).ToListAsync(cancellationToken);
        int classesChanged = 0;

        foreach (Job job in scoredJobs.Where(job => job.Score is ScoreCard card && card.Skills.Any(named => catalog.Resolve(named.Name) == changed)))
        {
            if (CompRecomputeService.Reclassify(job, settings, catalog))
            {
                classesChanged++;
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        return classesChanged;
    }
}
