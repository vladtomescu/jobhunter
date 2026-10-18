using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Pipeline;
using JobHunter.Sources;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Refresh;

/// <summary>What a run compares incoming postings against: the jobs it has already touched, by fingerprint, and the recent ones grouped by company for the fuzzy match.</summary>
/// <remarks>The index lives for one run: jobs it hands out are tracked by the run's context, so the run saves them all at once.</remarks>
internal sealed class MergeIndex
{
    private readonly Dictionary<string, Job> byFingerprint = new(StringComparer.Ordinal);

    private readonly Dictionary<string, List<DuplicateCandidate>> byCompany = new(StringComparer.Ordinal);

    private MergeIndex()
    {
    }

    /// <summary>Loads the jobs first seen inside the fuzzy-match window, which are the only ones a fuzzy match is believed for.</summary>
    public static async Task<MergeIndex> LoadAsync(JobHunterDbContext context, DateTimeOffset startedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        DateTimeOffset windowStart = startedAt.AddDays(-DuplicateMatcher.WindowDays);

        var recent = await context.Jobs
            .AsNoTracking()
            .Where(job => job.FirstSeenAt >= windowStart)
            .Select(job => new { job.Id, job.Company, job.Title, job.FirstSeenAt, job.DescriptionHash, job.Sources })
            .ToListAsync(cancellationToken);

        MergeIndex index = new();

        foreach (var job in recent)
        {
            index.Remember(new DuplicateCandidate(job.Id, job.Company, job.Title, job.FirstSeenAt, job.DescriptionHash, Kinds(job.Sources)));
        }

        return index;
    }

    /// <summary>Returns the job an incoming posting belongs to, by fingerprint first and by fuzzy match afterwards, or null when the posting is new.</summary>
    /// <remarks>A fuzzy match can name a job this run created and has not saved yet, the next copy of a posting listed once per country for example, so the match is looked up through the change tracker before the database.</remarks>
    public async Task<Job?> ResolveAsync(JobHunterDbContext context, string fingerprint, string company, string title, string descriptionHash, JobSourceKind source, DateTimeOffset seenAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (byFingerprint.TryGetValue(fingerprint, out Job? known))
        {
            return known;
        }

        Job? stored = await context.Jobs.FirstOrDefaultAsync(job => job.Fingerprint == fingerprint, cancellationToken);
        if (stored is not null)
        {
            byFingerprint[fingerprint] = stored;

            return stored;
        }

        if (FindFuzzyMatch(company, title, descriptionHash, source, seenAt) is not DuplicateCandidate match)
        {
            return null;
        }

        Job? matched = await context.Jobs.FindAsync([match.JobId], cancellationToken);
        if (matched is not null)
        {
            byFingerprint[fingerprint] = matched;
        }

        return matched;
    }

    /// <summary>Adds a job the run has just created, once its first source is recorded, so that a later posting resolves to it instead of creating a second one.</summary>
    public void Add(Job job)
    {
        ArgumentNullException.ThrowIfNull(job);

        byFingerprint[job.Fingerprint] = job;
        Remember(Candidate(job));
    }

    private static DuplicateCandidate Candidate(Job job)
    {
        return new DuplicateCandidate(job.Id, job.Company, job.Title, job.FirstSeenAt, job.DescriptionHash, Kinds(job.Sources));
    }

    private static JobSourceKind[] Kinds(IEnumerable<JobSourceRef> sources)
    {
        return [.. sources.Select(source => source.Kind).Distinct()];
    }

    private DuplicateCandidate? FindFuzzyMatch(string company, string title, string descriptionHash, JobSourceKind source, DateTimeOffset seenAt)
    {
        return byCompany.TryGetValue(DuplicateMatcher.NormalizeCompany(company), out List<DuplicateCandidate>? candidates)
            ? DuplicateMatcher.FindMatch(candidates, company, title, descriptionHash, source, seenAt)
            : null;
    }

    private void Remember(DuplicateCandidate candidate)
    {
        string key = DuplicateMatcher.NormalizeCompany(candidate.Company);

        if (!byCompany.TryGetValue(key, out List<DuplicateCandidate>? candidates))
        {
            candidates = [];
            byCompany[key] = candidates;
        }

        candidates.Add(candidate);
    }
}
