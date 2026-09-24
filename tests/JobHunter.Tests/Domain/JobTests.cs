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

    [Theory]
    [InlineData(90_000, 110_000, true)]
    [InlineData(90_000, 109_999, false)]
    [InlineData(null, 120_000, true)]
    [InlineData(null, 80_000, false)]
    [InlineData(110_000, null, true)]
    [InlineData(105_000, null, false)]
    [InlineData(null, null, false)]
    public void RecordCompensation_ForThePayInEuroAYear_RaisesTheHighPayFlagFromTheMaximumOrElseTheMinimum(int? minEurYear, int? maxEurYear, bool expected)
    {
        Job job = NewJob();

        job.RecordCompensation(minEurYear, maxEurYear, "EUR", CompPeriod.Year, minEurYear, maxEurYear);

        Assert.Equal(expected, job.Flags.Contains(JobFlag.HighPay));
    }

    [Fact]
    public void RecordCompensation_WhenThePayFallsBelowTheThreshold_ClearsTheHighPayFlag()
    {
        Job job = NewJob();
        job.RecordCompensation(null, 120_000m, "EUR", CompPeriod.Year, null, 120_000m);

        job.RecordCompensation(null, 80_000m, "EUR", CompPeriod.Year, null, 80_000m);

        Assert.DoesNotContain(JobFlag.HighPay, job.Flags);
    }

    [Fact]
    public void RecordCompensation_WhenThePayBecomesUnknown_ClearsTheHighPayFlag()
    {
        Job job = NewJob();
        job.RecordCompensation(null, 120_000m, "EUR", CompPeriod.Year, null, 120_000m);

        job.RecordCompensation(null, null, null, null, null, null);

        Assert.DoesNotContain(JobFlag.HighPay, job.Flags);
    }

    [Fact]
    public void ApplyPrefilterVerdict_ForAJobWhosePayReachesTheThreshold_KeepsTheHighPayFlag()
    {
        Job job = NewJob();
        job.RecordCompensation(null, 120_000m, "EUR", CompPeriod.Year, null, 120_000m);

        job.ApplyPrefilterVerdict(PrefilterState.Passed, null, [JobFlag.H1, JobFlag.CU]);

        Assert.Equal<JobFlag>([JobFlag.H1, JobFlag.CU, JobFlag.HighPay], job.Flags);
    }

    private static Job NewJob()
    {
        return Job.Create("fingerprint-1", "https://jobs.example.com/backend", "https://jobs.example.com/backend", "Example", "Backend Engineer", "Plain text description.", "hash-1", new DateTimeOffset(2026, 9, 14, 8, 0, 0, TimeSpan.Zero), isManual: false);
    }
}
