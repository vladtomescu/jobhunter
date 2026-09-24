using JobHunter.Domain;
using JobHunter.Jobs;
using JobHunter.Sources;

namespace JobHunter.Tests.Jobs;

/// <summary>Proves that a job entered by hand lands in the database normalized, flagged, never dropped and still waiting for a score.</summary>
public sealed class ManualJobServiceTests
{
    [Fact]
    public async Task CreateAsync_ForANewPosting_StoresAManualJobThatStillAwaitsItsScore()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();

        ManualJobResult result = await harness.ManualJobs.CreateAsync(new ManualJobRequest(
            "https://careers.example.com/jobs/42?utm_source=newsletter",
            "Senior Backend Engineer",
            "Northwind Labs",
            "We run distributed .NET services.",
            "Remote, Europe",
            null));

        Job job = await harness.GetJobAsync(result.JobId);

        Assert.False(result.AlreadyKnown);
        Assert.True(job.IsManual);
        Assert.Equal(ScoringState.Unscored, job.Scoring);
        Assert.Null(job.Class);
        Assert.Equal(PrefilterState.Passed, job.Prefilter);
        Assert.Equal("https://careers.example.com/jobs/42", job.CanonicalApplyUrl);
        Assert.Equal(JobSourceKind.Manual, Assert.Single(job.Sources).Kind);
        Assert.Equal("Remote, Europe", job.LocationText);
        Assert.Contains(JobFlag.H1, job.Flags);
        Assert.Contains(JobFlag.CU, job.Flags);
    }

    [Fact]
    public async Task CreateAsync_WithCompensationText_StoresTheFiguresNormalizedToEuroAYear()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();

        ManualJobResult result = await harness.ManualJobs.CreateAsync(new ManualJobRequest(
            "https://careers.example.com/jobs/43",
            "Backend Engineer",
            "Contoso",
            "Plain description.",
            null,
            "90,000 - 120,000 EUR per year"));

        Job job = await harness.GetJobAsync(result.JobId);

        Assert.Equal(90_000m, job.CompMin);
        Assert.Equal(120_000m, job.CompMax);
        Assert.Equal("EUR", job.CompCurrency);
        Assert.Equal(CompPeriod.Year, job.CompPeriod);
        Assert.Equal(90_000m, job.CompMinPerYear);
        Assert.Equal(120_000m, job.CompMaxPerYear);
        Assert.DoesNotContain(JobFlag.CU, job.Flags);
    }

    [Fact]
    public async Task CreateAsync_WithAnHourlyRate_AnnualizesTheFigure()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();

        ManualJobResult result = await harness.ManualJobs.CreateAsync(new ManualJobRequest(
            "https://careers.example.com/jobs/44",
            "Backend Engineer",
            "Fabrikam",
            "Plain description.",
            null,
            "60 EUR/h"));

        Job job = await harness.GetJobAsync(result.JobId);

        Assert.Equal(60m, job.CompMin);
        Assert.Equal(CompPeriod.Hour, job.CompPeriod);
        Assert.Equal(105_600m, job.CompMinPerYear);
    }

    [Fact]
    public async Task CreateAsync_WithAnHtmlDescription_StoresPlainText()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();

        ManualJobResult result = await harness.ManualJobs.CreateAsync(new ManualJobRequest(
            "https://careers.example.com/jobs/45",
            "Backend Engineer",
            "Tailspin",
            "<p>We run <strong>distributed</strong> services.</p>",
            null,
            null));

        Job job = await harness.GetJobAsync(result.JobId);

        Assert.DoesNotContain('<', job.DescriptionText);
        Assert.Contains("distributed", job.DescriptionText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_WithATitleTheRulesExclude_KeepsTheJobBecauseManualJobsNeverDrop()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();

        ManualJobResult result = await harness.ManualJobs.CreateAsync(new ManualJobRequest(
            "https://careers.example.com/jobs/46",
            "Senior Frontend Engineer",
            "Proseware",
            "Plain description.",
            null,
            null));

        Job job = await harness.GetJobAsync(result.JobId);

        Assert.Equal(PrefilterState.Passed, job.Prefilter);
        Assert.Null(job.DropReason);
    }

    [Fact]
    public async Task CreateAsync_ForALinkAlreadyStored_ReturnsTheKnownJobInsteadOfASecondOne()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();

        ManualJobRequest request = new("https://careers.example.com/jobs/47", "Backend Engineer", "Adventure Works", "Plain description.", null, null);

        ManualJobResult first = await harness.ManualJobs.CreateAsync(request);
        ManualJobResult second = await harness.ManualJobs.CreateAsync(request with { Title = "Backend Engineer II" });

        Assert.False(first.AlreadyKnown);
        Assert.True(second.AlreadyKnown);
        Assert.Equal(first.JobId, second.JobId);
    }
}
