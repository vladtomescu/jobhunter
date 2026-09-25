using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Jobs;
using JobHunter.Pipeline;
using JobHunter.Sources;
using Microsoft.EntityFrameworkCore;

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

    [Fact]
    public async Task UpdateAsync_ForAManualJob_ReplacesTheTypedFieldsThroughTheAddNormalization()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        ManualJobResult created = await harness.ManualJobs.CreateAsync(new ManualJobRequest("https://careers.example.com/jobs/50", "Backend Engineer", "Northwind Labs", "Plain description.", "Remote, Europe", null));

        ManualJobEditResult edit = await harness.ManualJobs.UpdateAsync(created.JobId, new ManualJobRequest(
            "https://careers.example.com/jobs/51?utm_source=newsletter",
            "Senior Platform Engineer",
            "Northwind Labs Europe",
            "Plain description.",
            null,
            "60 EUR/h"));

        Job job = await harness.GetJobAsync(created.JobId);
        Assert.True(edit.IsSaved);
        Assert.Equal("Senior Platform Engineer", job.Title);
        Assert.Equal("Northwind Labs Europe", job.Company);
        Assert.Null(job.LocationText);
        Assert.Equal("https://careers.example.com/jobs/51", job.CanonicalApplyUrl);
        Assert.Equal(JobFingerprint.ForCanonicalUrl("https://careers.example.com/jobs/51"), job.Fingerprint);
        Assert.Equal("https://careers.example.com/jobs/51", Assert.Single(job.Sources).SourceId);
        Assert.Equal(60m, job.CompMin);
        Assert.Equal(CompPeriod.Hour, job.CompPeriod);
        Assert.Equal(105_600m, job.CompMinPerYear);
        Assert.DoesNotContain(JobFlag.CU, job.Flags);
        Assert.Contains(JobFlag.H1, job.Flags);
        Assert.Equal(PrefilterState.Passed, job.Prefilter);
    }

    [Fact]
    public async Task UpdateAsync_WithTheFormAsLoaded_ChangesNothing()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        ManualJobResult created = await harness.ManualJobs.CreateAsync(new ManualJobRequest("https://careers.example.com/jobs/52", "Backend Engineer", "Contoso", "<p>We run <strong>distributed</strong> services.</p>", "Remote", "90,000 - 120,000 EUR per year"));
        Job before = await harness.GetJobAsync(created.JobId);

        ManualJobEditResult edit = await harness.ManualJobs.UpdateAsync(created.JobId, ManualJobRequest.FromJob(before));

        Job after = await harness.GetJobAsync(created.JobId);
        Assert.True(edit.IsSaved);
        Assert.Equal(before.DescriptionHash, after.DescriptionHash);
        Assert.Equal(before.Fingerprint, after.Fingerprint);
        Assert.Equal(90_000m, after.CompMin);
        Assert.Equal(120_000m, after.CompMax);
        Assert.Equal("EUR", after.CompCurrency);
        Assert.Equal(CompPeriod.Year, after.CompPeriod);
        Assert.Equal(before.Flags, after.Flags);
    }

    [Fact]
    public async Task UpdateAsync_WithNewPayOnAScoredJob_ReclassifiesFromTheStoredScoreCard()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SettingsService.ApplyAsync(settings => settings.ConfigureCompensation(45m, 65_000m, 120_000m));
        ManualJobRequest request = new("https://careers.example.com/jobs/53", "Platform Engineer", "Fabrikam", "Plain description.", null, "110,000 EUR per year");
        ManualJobResult created = await harness.ManualJobs.CreateAsync(request);
        await ScoreAsync(harness, created.JobId, requiresUsAuthorization: false, JobClass.A);

        ManualJobEditResult edit = await harness.ManualJobs.UpdateAsync(created.JobId, request with { CompText = "40,000 EUR per year" });

        Job job = await harness.GetJobAsync(created.JobId);
        Assert.True(edit.IsSaved);
        Assert.Equal(40_000m, job.CompMinPerYear);
        Assert.Equal(ScoringState.Scored, job.Scoring);
        Assert.NotNull(job.Score);
        Assert.Equal(0, job.Score.CompSignal);
        Assert.Equal(9, job.Score.Total);
        Assert.Equal(JobClass.C, job.Class);
    }

    [Fact]
    public async Task UpdateAsync_WithAnUnchangedDescriptionOnAScoredJob_KeepsTheScoreClassAndAuthorizationFlag()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        ManualJobRequest request = new("https://careers.example.com/jobs/54", "Platform Engineer", "Fabrikam", "Plain description.", null, null);
        ManualJobResult created = await harness.ManualJobs.CreateAsync(request);
        await ScoreAsync(harness, created.JobId, requiresUsAuthorization: true, JobClass.C);

        await harness.ManualJobs.UpdateAsync(created.JobId, request with { Title = "Staff Platform Engineer" });

        Job job = await harness.GetJobAsync(created.JobId);
        Assert.Equal("Staff Platform Engineer", job.Title);
        Assert.Equal(ScoringState.Scored, job.Scoring);
        Assert.Equal(JobClass.C, job.Class);
        Assert.Contains(JobFlag.WA, job.Flags);
    }

    [Fact]
    public async Task UpdateAsync_WithANewDescription_SendsTheJobBackForScoringLikeARewrittenPosting()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        ManualJobRequest request = new("https://careers.example.com/jobs/55", "Platform Engineer", "Fabrikam", "Plain description.", null, null);
        ManualJobResult created = await harness.ManualJobs.CreateAsync(request);
        await ScoreAsync(harness, created.JobId, requiresUsAuthorization: false, JobClass.A);

        await harness.ManualJobs.UpdateAsync(created.JobId, request with { DescriptionText = "<p>We now run the ingestion path on .NET.</p>" });

        Job job = await harness.GetJobAsync(created.JobId);
        Assert.Equal(ScoringState.Unscored, job.Scoring);
        Assert.Null(job.Class);
        Assert.DoesNotContain('<', job.DescriptionText);
        Assert.Equal(JobFingerprint.ForDescription(job.DescriptionText), job.DescriptionHash);
    }

    [Fact]
    public async Task UpdateAsync_ForAJobFromASource_RefusesAndChangesNothing()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        Job board = ListedJobs.NewJob("Tailspin", "Backend Engineer", DateTimeOffset.UtcNow);
        await harness.SaveAsync(board);

        ManualJobEditResult edit = await harness.ManualJobs.UpdateAsync(board.Id, new ManualJobRequest("https://careers.example.com/jobs/56", "Changed Title", "Tailspin", null, null, null));

        Assert.False(edit.IsSaved);
        Assert.Equal("Backend Engineer", (await harness.GetJobAsync(board.Id)).Title);
    }

    [Fact]
    public async Task UpdateAsync_WithALinkAnotherJobHolds_RefusesAndChangesNothing()
    {
        await using JobsTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.ManualJobs.CreateAsync(new ManualJobRequest("https://careers.example.com/jobs/57", "Backend Engineer", "Proseware", null, null, null));
        ManualJobResult second = await harness.ManualJobs.CreateAsync(new ManualJobRequest("https://careers.example.com/jobs/58", "Backend Engineer", "Proseware", null, null, null));

        ManualJobEditResult edit = await harness.ManualJobs.UpdateAsync(second.JobId, new ManualJobRequest("https://careers.example.com/jobs/57", "Backend Engineer", "Proseware", null, null, null));

        Assert.False(edit.IsSaved);
        Assert.Equal("https://careers.example.com/jobs/58", (await harness.GetJobAsync(second.JobId)).CanonicalApplyUrl);
    }

    private static async Task ScoreAsync(JobsTestHarness harness, Guid jobId, bool requiresUsAuthorization, JobClass jobClass)
    {
        await using JobHunterDbContext context = await harness.ContextFactory.CreateDbContextAsync();
        Job job = await context.Jobs.SingleAsync(candidate => candidate.Id == jobId);

        ScoreCard card = new(
            2, 2, 2, 1, 1, 1, 1, 10,
            "Fit.", "senior", "remote", "b2b", job.CompMin, job.CompMax, job.CompCurrency, job.CompPeriod,
            string.Empty, requiresUsAuthorization, true, string.Empty, [], "test-model", DateTimeOffset.UtcNow, job.DescriptionHash);
        job.RecordWorkAuthorizationRequirement(requiresUsAuthorization);
        job.RecordScore(card, jobClass);

        await context.SaveChangesAsync();
    }
}
