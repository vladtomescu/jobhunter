using JobHunter.Domain;

namespace JobHunter.Tests.Llm;

/// <summary>Builds the jobs the exchange tests export and import, with every field a line carries filled in.</summary>
internal static class LlmTestJobs
{
    /// <summary>The moment every test job is first seen at.</summary>
    public static readonly DateTimeOffset SeenAt = new(2026, 9, 14, 8, 0, 0, TimeSpan.Zero);

    /// <summary>A job that passed the prefilter and is still waiting for a score.</summary>
    public static Job NewUnscoredJob(string company = "Northwind Labs", string title = "Senior Backend Engineer")
    {
        Job job = Job.Create(
            $"fp-{Guid.NewGuid():N}",
            $"https://jobs.example.com/{Guid.NewGuid():N}",
            "https://jobs.example.com/posting",
            company,
            title,
            "Plain text description.",
            $"hash-{Guid.NewGuid():N}",
            SeenAt,
            isManual: false);

        job.RecordPlace("Remote, Europe", "DE", "Europe", true, "en");
        job.RecordPostingFacts("https://jobs.example.com/apply", null, AtsKind.Greenhouse, ["kotlin"], "b2b", SeenAt.AddDays(-3));
        job.RecordCompensation(90_000m, 120_000m, "EUR", CompPeriod.Year, 90_000m, 120_000m);
        job.ApplyPrefilterVerdict(PrefilterState.Passed, null, [JobFlag.H1]);

        return job;
    }

    /// <summary>A job that was scored and then pursued, which is what a kit is written for.</summary>
    public static Job NewPursuedJob(string company = "Northwind Labs")
    {
        Job job = NewUnscoredJob(company);
        job.RecordScore(NewScoreCard(), JobClass.A);
        job.Pursue(SeenAt);

        return job;
    }

    /// <summary>A score card with a class-A total, as a scored job carries it.</summary>
    public static ScoreCard NewScoreCard()
    {
        return new ScoreCard(2, 2, 2, 1, 2, 2, 1, 12, "Strong platform fit.", "senior", "remote", "b2b", 90_000m, 120_000m, "EUR", CompPeriod.Year, "Overlaps European hours.", false, true, "Agent tooling.", ["timezone"], "claude-opus-5", SeenAt, "hash-1");
    }
}
