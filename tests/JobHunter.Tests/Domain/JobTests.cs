using JobHunter.Domain;

namespace JobHunter.Tests.Domain;

/// <summary>Covers the job aggregate's state transitions that later stages depend on.</summary>
public sealed class JobTests
{
    [Fact]
    public void ApplyPrefilterVerdict_WhenADroppedJobPassesLater_ClearsTheClass()
    {
        Job job = Job.Create("fingerprint-1", "https://jobs.example.com/backend", "https://jobs.example.com/backend", "Example", "Backend Engineer", "Plain text description.", "hash-1", new DateTimeOffset(2026, 9, 14, 8, 0, 0, TimeSpan.Zero), isManual: false);
        job.ApplyPrefilterVerdict(PrefilterState.Dropped, "title excluded", []);
        Assert.Equal(JobClass.D, job.Class);

        job.ApplyPrefilterVerdict(PrefilterState.Passed, null, [JobFlag.CU]);

        Assert.Null(job.Class);
        Assert.Equal(PrefilterState.Passed, job.Prefilter);
        Assert.Null(job.DropReason);
    }
}
