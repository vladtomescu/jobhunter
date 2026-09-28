using JobHunter.Applications;
using JobHunter.Domain;
using JobHunter.Llm.Contracts;

namespace JobHunter.Tests.Applications;

/// <summary>Covers the one check-and-store step both cover-letter paths share, and the model path that feeds it.</summary>
public sealed class CoverLetterServiceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset SeenAt = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly ApplicationsTestHarness harness = new();

    public async Task InitializeAsync()
    {
        await harness.InitializeAsync();
        await harness.Settings.ApplyAsync(settings => settings.ConfigureCandidate(
            "NL",
            "Utrecht",
            acceptEuropeRemote: true,
            acceptUnitedStatesRemote: true,
            "en,nl",
            "EUR",
            "Java, Kotlin",
            ContractPreference.Either,
            hasUnitedStatesWorkAuthorization: false,
            highPayThresholdPerYear: null,
            JobHunter.Domain.Settings.DefaultTitleIncludeTerms,
            JobHunter.Domain.Settings.DefaultTitleExcludeTerms));
    }

    public Task DisposeAsync()
    {
        return harness.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task StoreAsync_OnASavedJob_StoresTheLetterWithTheModelAndNoLintIssues()
    {
        Job job = await SaveJobAsync(pursue: true);

        CoverLetterStoreResult result = await harness.CoverLetters.StoreAsync(FakeCoverLetterWriter.CleanLetter(job.Id), "claude-code", null);

        Assert.True(result.IsStored);
        Assert.Equal(job.Id, result.JobId);
        Assert.Empty(result.LintIssues);
        ApplicationCoverLetter stored = (await GetApplicationAsync(job.Id)).CoverLetter!;
        Assert.Equal("claude-code", stored.Model);
        Assert.Equal("en", stored.Language);
        Assert.Equal("Dear Example Co team,", stored.Salutation);
        Assert.Equal(3, stored.Paragraphs.Count);
        Assert.Equal("Kind regards,", stored.Closing);
        Assert.Empty(stored.LintIssues);
    }

    [Fact]
    public async Task StoreAsync_OnAJobThatIsNotSaved_RefusesWithTheReasonAndStoresNothing()
    {
        Job job = await SaveJobAsync(pursue: false);

        CoverLetterStoreResult result = await harness.CoverLetters.StoreAsync(FakeCoverLetterWriter.CleanLetter(job.Id), "claude-code", null);

        Assert.False(result.IsStored);
        Assert.Contains("is not saved", result.Refusal, StringComparison.Ordinal);
        Assert.Contains("save the job first", result.Refusal, StringComparison.Ordinal);
        Assert.Equal(0, await harness.CountApplicationsAsync());
    }

    [Fact]
    public async Task StoreAsync_OnAnUnknownJob_RefusesWithTheReason()
    {
        CoverLetterStoreResult result = await harness.CoverLetters.StoreAsync(FakeCoverLetterWriter.CleanLetter(Guid.NewGuid()), "claude-code", null);

        Assert.Contains("no job is stored", result.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoreAsync_WithAJobIdThatIsNotAnIdentifier_Refuses()
    {
        CoverLetterPayload letter = FakeCoverLetterWriter.CleanLetter(Guid.NewGuid()) with { JobId = "job-7" };

        CoverLetterStoreResult result = await harness.CoverLetters.StoreAsync(letter, "claude-code", null);

        Assert.Contains("job_id job-7 is not an identifier", result.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoreAsync_WithAJobIdOtherThanTheJobTheLetterWasWrittenFor_RefusesAndStoresNothing()
    {
        Job asked = await SaveJobAsync(pursue: true);
        Job named = await SaveJobAsync(pursue: true);

        CoverLetterStoreResult result = await harness.CoverLetters.StoreAsync(FakeCoverLetterWriter.CleanLetter(named.Id), "claude-opus-5", asked.Id);

        Assert.Contains("does not match the job", result.Refusal, StringComparison.Ordinal);
        Assert.Null((await GetApplicationAsync(asked.Id)).CoverLetter);
        Assert.Null((await GetApplicationAsync(named.Id)).CoverLetter);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(8)]
    public async Task StoreAsync_WithTooFewOrTooManyParagraphs_RefusesAndNamesTheBounds(int paragraphCount)
    {
        Job job = await SaveJobAsync(pursue: true);
        CoverLetterPayload letter = FakeCoverLetterWriter.CleanLetter(job.Id) with { Paragraphs = [.. Enumerable.Range(1, paragraphCount).Select(number => $"Paragraph {number} of the letter.")] };

        CoverLetterStoreResult result = await harness.CoverLetters.StoreAsync(letter, "claude-code", null);

        Assert.Contains($"has {paragraphCount} paragraphs", result.Refusal, StringComparison.Ordinal);
        Assert.Contains("3 to 7", result.Refusal, StringComparison.Ordinal);
        Assert.Null((await GetApplicationAsync(job.Id)).CoverLetter);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(3)]
    public async Task StoreAsync_WithParagraphsAtTheBounds_StoresTheLetter(int paragraphCount)
    {
        Job job = await SaveJobAsync(pursue: true);
        CoverLetterPayload letter = FakeCoverLetterWriter.CleanLetter(job.Id) with { Paragraphs = [.. Enumerable.Range(1, paragraphCount).Select(number => $"Paragraph {number} of the letter.")] };

        CoverLetterStoreResult result = await harness.CoverLetters.StoreAsync(letter, "claude-code", null);

        Assert.True(result.IsStored);
        Assert.Equal(paragraphCount, (await GetApplicationAsync(job.Id)).CoverLetter!.Paragraphs.Count);
    }

    [Theory]
    [InlineData("salutation", "salutation is empty")]
    [InlineData("closing", "closing is empty")]
    [InlineData("paragraph", "paragraphs[1] is blank")]
    [InlineData("language", "is not one of the accepted languages")]
    [InlineData("code", "is not a two-letter lower-case ISO 639-1 code")]
    public async Task StoreAsync_WithALetterOutsideTheContract_RefusesWithTheReason(string slip, string expectedReason)
    {
        Job job = await SaveJobAsync(pursue: true);
        CoverLetterPayload clean = FakeCoverLetterWriter.CleanLetter(job.Id);
        CoverLetterPayload letter = slip switch
        {
            "salutation" => clean with { Salutation = " " },
            "closing" => clean with { Closing = string.Empty },
            "paragraph" => clean with { Paragraphs = [clean.Paragraphs[0], "  ", clean.Paragraphs[2]] },
            "language" => clean with { Language = "de" },
            _ => clean with { Language = "English" }
        };

        CoverLetterStoreResult result = await harness.CoverLetters.StoreAsync(letter, "claude-code", null);

        Assert.Contains(expectedReason, result.Refusal, StringComparison.Ordinal);
        Assert.Null((await GetApplicationAsync(job.Id)).CoverLetter);
    }

    [Fact]
    public async Task StoreAsync_WithALetterInAnotherAcceptedLanguage_StoresIt()
    {
        Job job = await SaveJobAsync(pursue: true);

        CoverLetterStoreResult result = await harness.CoverLetters.StoreAsync(FakeCoverLetterWriter.CleanLetter(job.Id) with { Language = "nl" }, "claude-code", null);

        Assert.True(result.IsStored);
        Assert.Equal("nl", (await GetApplicationAsync(job.Id)).CoverLetter!.Language);
    }

    [Fact]
    public async Task StoreAsync_WithALetterThatBreaksTheVoiceRules_KeepsTheTextAndRecordsTheIssues()
    {
        Job job = await SaveJobAsync(pursue: true);
        CoverLetterPayload clean = FakeCoverLetterWriter.CleanLetter(job.Id);
        CoverLetterPayload letter = clean with { Paragraphs = [clean.Paragraphs[0], "Call me on +1 555 010 2233, I would love this role!", clean.Paragraphs[2]] };

        CoverLetterStoreResult result = await harness.CoverLetters.StoreAsync(letter, "claude-code", null);

        Assert.True(result.IsStored);
        Assert.Equal(2, result.LintIssues.Count);
        Assert.All(result.LintIssues, issue => Assert.StartsWith("paragraphs[1]:", issue, StringComparison.Ordinal));
        ApplicationCoverLetter stored = (await GetApplicationAsync(job.Id)).CoverLetter!;
        Assert.Equal(result.LintIssues, stored.LintIssues);
        Assert.Equal("Call me on +1 555 010 2233, I would love this role!", stored.Paragraphs[1]);
    }

    [Fact]
    public async Task StoreAsync_OnAJobThatAlreadyHasALetter_ReplacesIt()
    {
        Job job = await SaveJobAsync(pursue: true);
        await harness.CoverLetters.StoreAsync(FakeCoverLetterWriter.CleanLetter(job.Id), "claude-opus-5", null);
        CoverLetterPayload rewrite = FakeCoverLetterWriter.CleanLetter(job.Id) with { Salutation = "Dear hiring team,", Paragraphs = ["First.", "Second.", "Third.", "Fourth."] };

        CoverLetterStoreResult result = await harness.CoverLetters.StoreAsync(rewrite, "claude-code", null);

        Assert.True(result.IsStored);
        ApplicationCoverLetter stored = (await GetApplicationAsync(job.Id)).CoverLetter!;
        Assert.Equal("Dear hiring team,", stored.Salutation);
        Assert.Equal<string>(["First.", "Second.", "Third.", "Fourth."], stored.Paragraphs);
        Assert.Equal("claude-code", stored.Model);
    }

    [Fact]
    public async Task StoreAsync_OnAJobWithAKit_LeavesTheKitAlone()
    {
        Job job = await SaveJobAsync(pursue: true);
        await harness.Triage.WriteKitAsync(job.Id);
        Application before = await GetApplicationAsync(job.Id);

        await harness.CoverLetters.StoreAsync(FakeCoverLetterWriter.CleanLetter(job.Id), "claude-code", null);

        Application after = await GetApplicationAsync(job.Id);
        Assert.Equal(KitState.Ready, after.KitState);
        Assert.Equal(before.Kit!.CoverNote, after.Kit!.CoverNote);
        Assert.NotNull(after.CoverLetter);
    }

    [Fact]
    public async Task WriteAsync_OnASavedScoredJob_SendsTheKitRequestOnTheKitModelAndStoresTheLetter()
    {
        Job job = await SaveJobAsync(pursue: true);
        JobHunter.Domain.Settings settings = await harness.Settings.GetAsync();

        CoverLetterStoreResult result = await harness.CoverLetters.WriteAsync(job.Id);

        KitRequest request = Assert.Single(harness.CoverLetterWriter.Requests);
        Assert.Equal(job.Id, request.JobId);
        Assert.Equal(settings.KitModel, request.Model);
        Assert.Equal("A", request.Class);
        Assert.Equal<string>(["timezone"], request.Score.BlockingUnknowns);
        Assert.True(result.IsStored);
        Assert.Equal(settings.KitModel, (await GetApplicationAsync(job.Id)).CoverLetter!.Model);
    }

    [Fact]
    public async Task WriteAsync_OnAnUnscoredSavedJob_RefusesWithoutCallingTheWriter()
    {
        Job job = TestJobs.NewJob("Example Co", [], null, isActive: true, SeenAt);
        await harness.SaveAsync(job);
        await harness.Triage.PursueAsync(job.Id);

        CoverLetterStoreResult result = await harness.CoverLetters.WriteAsync(job.Id);

        Assert.Contains("has not been scored", result.Refusal, StringComparison.Ordinal);
        Assert.Equal(0, harness.CoverLetterWriter.CallCount);
    }

    [Fact]
    public async Task WriteAsync_OnAJobThatIsNotSaved_RefusesWithoutCallingTheWriter()
    {
        Job job = await SaveJobAsync(pursue: false);

        CoverLetterStoreResult result = await harness.CoverLetters.WriteAsync(job.Id);

        Assert.Contains("is not saved", result.Refusal, StringComparison.Ordinal);
        Assert.Equal(0, harness.CoverLetterWriter.CallCount);
    }

    [Fact]
    public async Task WriteAsync_WhenTheWriterFails_ReturnsTheReasonAndStoresNothing()
    {
        Job job = await SaveJobAsync(pursue: true);
        harness.CoverLetterWriter.EnqueueOutcome(_ => CoverLetterOutcome.Failure("rate limited", retryable: true));

        CoverLetterStoreResult result = await harness.CoverLetters.WriteAsync(job.Id);

        Assert.Equal("rate limited", result.Refusal);
        Assert.Null((await GetApplicationAsync(job.Id)).CoverLetter);
    }

    [Fact]
    public async Task WriteAsync_WhenTheWriterThrows_ReturnsTheMessageAndStoresNothing()
    {
        Job job = await SaveJobAsync(pursue: true);
        harness.CoverLetterWriter.ExceptionToThrowOnNextCall = new InvalidOperationException("network unreachable");

        CoverLetterStoreResult result = await harness.CoverLetters.WriteAsync(job.Id);

        Assert.Equal("network unreachable", result.Refusal);
        Assert.Null((await GetApplicationAsync(job.Id)).CoverLetter);
    }

    [Fact]
    public async Task WriteAsync_WhenTheModelNamesAnotherJob_RefusesTheLetter()
    {
        Job job = await SaveJobAsync(pursue: true);
        harness.CoverLetterWriter.EnqueueOutcome(request => CoverLetterOutcome.Success(FakeCoverLetterWriter.CleanLetter(Guid.NewGuid()), request.Model, LlmUsage.None));

        CoverLetterStoreResult result = await harness.CoverLetters.WriteAsync(job.Id);

        Assert.Contains("does not match the job", result.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FindJobAsync_ForEachKindOfJob_ReturnsTheRequestOrTheBlock()
    {
        Job saved = await SaveJobAsync(pursue: true);
        Job unsaved = await SaveJobAsync(pursue: false);

        CoverLetterJob found = await harness.CoverLetters.FindJobAsync(saved.Id, "claude-code");
        CoverLetterJob notSaved = await harness.CoverLetters.FindJobAsync(unsaved.Id, "claude-code");
        CoverLetterJob unknown = await harness.CoverLetters.FindJobAsync(Guid.NewGuid(), "claude-code");

        Assert.Equal(saved.Id, found.Request!.JobId);
        Assert.Equal("claude-code", found.Request.Model);
        Assert.Null(found.Block);
        Assert.Equal(CoverLetterBlock.NotSaved, notSaved.Block);
        Assert.Equal(CoverLetterBlock.UnknownJob, unknown.Block);
    }

    private async Task<Job> SaveJobAsync(bool pursue)
    {
        Job job = TestJobs.NewScoredJob("Example Co", JobClass.A, SeenAt);
        await harness.SaveAsync(job);

        if (pursue)
        {
            await harness.Triage.PursueAsync(job.Id);
        }

        return job;
    }

    private async Task<Application> GetApplicationAsync(Guid jobId)
    {
        return (await harness.FindApplicationByJobAsync(jobId))!;
    }
}
