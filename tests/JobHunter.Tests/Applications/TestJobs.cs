using JobHunter.Domain;
using JobHunter.Sources;

namespace JobHunter.Tests.Applications;

/// <summary>Builds jobs for the applications tests without dragging in the pipeline; every job gets a unique fingerprint and URL.</summary>
internal static class TestJobs
{
    /// <summary>A job already scored and classified, ready to be pursued.</summary>
    public static Job NewScoredJob(string company, JobClass jobClass, DateTimeOffset seenAt)
    {
        Job job = NewJob(company, [], null, isActive: true, seenAt);
        job.ApplyPrefilterVerdict(PrefilterState.Passed, null, [JobFlag.H1]);
        job.RecordScore(NewScoreCard(seenAt), jobClass);

        return job;
    }

    /// <summary>A job carrying the given sources and ATS, active or inactive, for the stats and ghost-candidate tests.</summary>
    public static Job NewJob(string company, IEnumerable<JobSourceKind> sources, AtsKind? ats, bool isActive, DateTimeOffset seenAt, bool isManual = false)
    {
        Job job = Job.Create(
            $"fp-{Guid.NewGuid():N}",
            $"https://jobs.example.com/{Guid.NewGuid():N}",
            $"https://jobs.example.com/{Guid.NewGuid():N}",
            company,
            "Backend Engineer",
            "Plain text description.",
            $"hash-{Guid.NewGuid():N}",
            seenAt,
            isManual);

        foreach (JobSourceKind kind in sources)
        {
            job.RecordSource(kind, $"source-{Guid.NewGuid():N}", seenAt);
        }

        if (ats is AtsKind atsKind)
        {
            job.RecordPostingFacts(null, null, atsKind, [], null, null);
        }

        if (!isActive)
        {
            job.MissRun();
            job.MissRun();
        }

        return job;
    }

    /// <summary>A score card with a fixed, class-A total.</summary>
    public static ScoreCard NewScoreCard(DateTimeOffset scoredAt)
    {
        return new ScoreCard(2, 2, 2, 1, 2, 2, 1, 12, "Strong platform fit.", "senior", "remote", "b2b", null, null, null, null, "Overlaps European hours.", false, true, "Agent tooling.", ["timezone"], "claude-opus-5", scoredAt, "hash-1");
    }
}
