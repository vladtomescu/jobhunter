using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JobHunter.Domain;
using JobHunter.Llm.Exchange;

namespace JobHunter.Tests.Llm;

/// <summary>Proves the backup path: the export writes what the repository skills read, and the import stores what they wrote through the same code the interface path uses.</summary>
public sealed class ExchangeTests
{
    [Fact]
    public async Task ExportAsync_WithAnUnscoredJob_WritesTheScoreLineInTheShapeTheSkillReads()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = LlmTestJobs.NewUnscoredJob();
        await harness.SaveAsync(job);

        ExchangeExportResult result = await harness.Exporter.ExportAsync();

        Assert.Equal(1, result.JobsToScore);
        JsonElement line = JsonDocument.Parse(Assert.Single(await harness.ReadExchangeLinesAsync(ExchangeFiles.ToScore))).RootElement;
        Assert.Equal<string>(
            ["job_id", "title", "company", "location", "remote_hint", "employment_hint", "comp_text", "posted_at", "flags", "description"],
            [.. line.EnumerateObject().Select(property => property.Name)]);
        Assert.Equal(job.Id.ToString(), line.GetProperty("job_id").GetString());
        Assert.Equal("Remote, Europe", line.GetProperty("location").GetString());
        Assert.Equal("remote", line.GetProperty("remote_hint").GetString());
        Assert.Equal("b2b", line.GetProperty("employment_hint").GetString());
        Assert.Equal("90000-120000 EUR per year", line.GetProperty("comp_text").GetString());
        Assert.Equal<string>(["H1", "HighPay"], [.. line.GetProperty("flags").EnumerateArray().Select(flag => flag.GetString() ?? string.Empty)]);
        Assert.Equal("Plain text description.", line.GetProperty("description").GetString());
    }

    [Fact]
    public async Task ExportAsync_WithAnUnscoredJob_WritesPlainTextWithoutAMarkerOrCarriageReturns()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(LlmTestJobs.NewUnscoredJob());

        await harness.Exporter.ExportAsync();

        byte[] written = await File.ReadAllBytesAsync(harness.ExchangeFile(ExchangeFiles.ToScore));
        Assert.NotEqual<byte>([0xEF, 0xBB, 0xBF], written.Take(3).ToArray());
        Assert.DoesNotContain((byte)'\r', written);
        Assert.Equal((byte)'\n', written[^1]);
        Assert.NotEmpty(Encoding.UTF8.GetString(written));
    }

    [Fact]
    public async Task ExportAsync_WithAScoredJob_LeavesItOutOfTheScoreFile()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(LlmTestJobs.NewPursuedJob());

        ExchangeExportResult result = await harness.Exporter.ExportAsync();

        Assert.Equal(0, result.JobsToScore);
        Assert.Empty(await harness.ReadExchangeLinesAsync(ExchangeFiles.ToScore));
    }

    [Fact]
    public async Task ExportAsync_WithAPursuedJobWithoutAKit_WritesTheKitLineInTheShapeTheSkillReads()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = LlmTestJobs.NewPursuedJob();
        await harness.SaveAsync(job);
        await harness.SaveAsync(Application.Create(job.Id, ApplicationStatus.Saved, LlmTestJobs.SeenAt, "Pursued from the inbox."));

        ExchangeExportResult result = await harness.Exporter.ExportAsync();

        Assert.Equal(1, result.JobsToKit);
        JsonElement line = JsonDocument.Parse(Assert.Single(await harness.ReadExchangeLinesAsync(ExchangeFiles.ToKit))).RootElement;
        Assert.Equal<string>(
            ["job_id", "title", "company", "apply_url", "description", "score", "class", "flags", "language_hint"],
            [.. line.EnumerateObject().Select(property => property.Name)]);
        Assert.Equal("A", line.GetProperty("class").GetString());
        Assert.Equal("en", line.GetProperty("language_hint").GetString());
        Assert.Equal("https://jobs.example.com/apply", line.GetProperty("apply_url").GetString());
        Assert.Equal(2, line.GetProperty("score").GetProperty("scores").GetProperty("niche").GetInt32());
        Assert.Equal("senior", line.GetProperty("score").GetProperty("facts").GetProperty("level_guess").GetString());
    }

    [Fact]
    public async Task ExportAsync_WithAKitAlreadyWritten_LeavesThatJobOutOfTheKitFile()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = LlmTestJobs.NewPursuedJob();
        await harness.SaveAsync(job);
        Application application = Application.Create(job.Id, ApplicationStatus.Saved, LlmTestJobs.SeenAt, "Pursued from the inbox.");
        application.AttachKit(new ApplicationKit(["A fact."], "A cover note.", ["A question?"], "resume.pdf", "en", LlmTestJobs.SeenAt, "claude-opus-5", []));
        await harness.SaveAsync(application);

        ExchangeExportResult result = await harness.Exporter.ExportAsync();

        Assert.Equal(0, result.JobsToKit);
    }

    [Fact]
    public async Task ExportAsync_WithAKitThatFailedTheLint_WritesItAgainSoItCanBeRetried()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = LlmTestJobs.NewPursuedJob();
        await harness.SaveAsync(job);
        Application application = Application.Create(job.Id, ApplicationStatus.Saved, LlmTestJobs.SeenAt, "Pursued from the inbox.");
        application.AttachKit(new ApplicationKit(["A fact."], "A cover note!", ["A question?"], "resume.pdf", "en", LlmTestJobs.SeenAt, "claude-opus-5", ["cover_note: exclamation mark"]));
        application.FailKit("cover_note: exclamation mark");
        await harness.SaveAsync(application);

        ExchangeExportResult result = await harness.Exporter.ExportAsync();

        Assert.Equal(1, result.JobsToKit);
    }

    [Fact]
    public async Task ExportAsync_WithAResumeOnDisk_CopiesItIntoTheExchangeFolder()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        string resume = Path.Combine(harness.Paths.Root, "resume-source.md");
        await File.WriteAllTextAsync(resume, "# Resume\n\nEverything I have done.");
        await harness.Settings.ApplyAsync(settings => settings.ConfigureResume("resume.pdf", resume));

        ExchangeExportResult result = await harness.Exporter.ExportAsync();

        Assert.True(result.ResumeCopied);
        Assert.Contains("Everything I have done.", await File.ReadAllTextAsync(harness.ExchangeFile(ExchangeFiles.Resume)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_WithAResumePathThatPointsNowhere_ReportsThatNothingWasCopied()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.Settings.ApplyAsync(settings => settings.ConfigureResume("resume.pdf", Path.Combine(harness.Paths.Root, "no-resume-here.md")));

        ExchangeExportResult result = await harness.Exporter.ExportAsync();

        Assert.False(result.ResumeCopied);
        Assert.False(File.Exists(harness.ExchangeFile(ExchangeFiles.Resume)));
    }

    [Fact]
    public async Task ImportAsync_WithAValidScoreLine_StoresTheScoreWithTheSkillAsTheModel()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = LlmTestJobs.NewUnscoredJob();
        await harness.SaveAsync(job);
        await harness.WriteExchangeFileAsync(ExchangeFiles.Scored, ScoreLine(job.Id));

        ExchangeImportResult result = await harness.Importer.ImportAsync();

        Assert.Equal(1, result.ScoresImported);
        Assert.Empty(result.Rejections);
        Job stored = await harness.GetJobAsync(job.Id);
        Assert.Equal(ScoringState.Scored, stored.Scoring);
        Assert.NotNull(stored.Score);
        Assert.Equal("claude-code", stored.Score.Model);
        Assert.Equal("senior", stored.Score.LevelGuess);
        Assert.Equal(12, stored.Score.Total);
    }

    [Fact]
    public async Task ImportAsync_WithAValidScoreLine_ComputesTheClassInCode()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job blocked = LlmTestJobs.NewUnscoredJob("Blocked Labs");
        Job open = LlmTestJobs.NewUnscoredJob("Open Labs");
        await harness.SaveAsync(blocked, open);
        await harness.WriteExchangeFileAsync(
            ExchangeFiles.Scored,
            ScoreLine(blocked.Id),
            ScoreLine(open.Id, payload => payload["blocking_unknowns"] = new JsonArray()));

        ExchangeImportResult result = await harness.Importer.ImportAsync();

        Assert.Equal(2, result.ScoresImported);
        Assert.Equal(JobClass.B, (await harness.GetJobAsync(blocked.Id)).Class);
        Assert.Equal(JobClass.A, (await harness.GetJobAsync(open.Id)).Class);
    }

    [Fact]
    public async Task ImportAsync_WithAMalformedLine_RejectsThatLineAndKeepsTheOthers()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = LlmTestJobs.NewUnscoredJob();
        await harness.SaveAsync(job);
        await harness.WriteExchangeFileAsync(ExchangeFiles.Scored, "{ not json at all", ScoreLine(job.Id));

        ExchangeImportResult result = await harness.Importer.ImportAsync();

        Assert.Equal(1, result.ScoresImported);
        ExchangeLineRejection rejection = Assert.Single(result.Rejections);
        Assert.Equal(ExchangeFiles.Scored, rejection.File);
        Assert.Equal(1, rejection.LineNumber);
        Assert.Contains("does not match the score schema", rejection.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_WithALineMissingARequiredProperty_RejectsThatLine()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = LlmTestJobs.NewUnscoredJob();
        await harness.SaveAsync(job);
        await harness.WriteExchangeFileAsync(ExchangeFiles.Scored, ScoreLine(job.Id, payload => payload.Remove("facts")));

        ExchangeImportResult result = await harness.Importer.ImportAsync();

        Assert.Equal(0, result.ScoresImported);
        Assert.Single(result.Rejections);
        Assert.Equal(ScoringState.Unscored, (await harness.GetJobAsync(job.Id)).Scoring);
    }

    [Fact]
    public async Task ImportAsync_WithAScoreOutsideTheRange_RejectsThatLine()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = LlmTestJobs.NewUnscoredJob();
        await harness.SaveAsync(job);
        await harness.WriteExchangeFileAsync(ExchangeFiles.Scored, ScoreLine(job.Id, payload => payload["scores"]!["niche"] = 4));

        ExchangeImportResult result = await harness.Importer.ImportAsync();

        Assert.Equal(0, result.ScoresImported);
        Assert.Contains("niche", Assert.Single(result.Rejections).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_WithAnOutOfVocabularyEmploymentType_RejectsThatLine()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = LlmTestJobs.NewUnscoredJob();
        await harness.SaveAsync(job);
        await harness.WriteExchangeFileAsync(ExchangeFiles.Scored, ScoreLine(job.Id, payload => payload["facts"]!["employment_type"] = "freelance"));

        ExchangeImportResult result = await harness.Importer.ImportAsync();

        Assert.Equal(0, result.ScoresImported);
        ExchangeLineRejection rejection = Assert.Single(result.Rejections);
        Assert.Contains("employment_type", rejection.Reason, StringComparison.Ordinal);
        Assert.Contains("freelance", rejection.Reason, StringComparison.Ordinal);
        Assert.Equal(ScoringState.Unscored, (await harness.GetJobAsync(job.Id)).Scoring);
    }

    [Fact]
    public async Task ImportAsync_WithAnUnmappedPropertyInTheScoreLine_RejectsThatLine()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = LlmTestJobs.NewUnscoredJob();
        await harness.SaveAsync(job);
        await harness.WriteExchangeFileAsync(ExchangeFiles.Scored, ScoreLine(job.Id, payload => payload["confidence"] = 0.9));

        ExchangeImportResult result = await harness.Importer.ImportAsync();

        Assert.Equal(0, result.ScoresImported);
        ExchangeLineRejection rejection = Assert.Single(result.Rejections);
        Assert.Contains("does not match the score schema", rejection.Reason, StringComparison.Ordinal);
        Assert.Equal(ScoringState.Unscored, (await harness.GetJobAsync(job.Id)).Scoring);
    }

    [Fact]
    public async Task ImportAsync_WithAnOutOfVocabularyLine_RejectsThatLineAndKeepsTheOthers()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job invalid = LlmTestJobs.NewUnscoredJob("Bad Vocabulary Labs");
        Job valid = LlmTestJobs.NewUnscoredJob("Good Vocabulary Labs");
        await harness.SaveAsync(invalid, valid);
        await harness.WriteExchangeFileAsync(
            ExchangeFiles.Scored,
            ScoreLine(invalid.Id, payload => payload["facts"]!["remote_policy"] = "asynchronous"),
            ScoreLine(valid.Id));

        ExchangeImportResult result = await harness.Importer.ImportAsync();

        Assert.Equal(1, result.ScoresImported);
        ExchangeLineRejection rejection = Assert.Single(result.Rejections);
        Assert.Equal(1, rejection.LineNumber);
        Assert.Contains("remote_policy", rejection.Reason, StringComparison.Ordinal);
        Assert.Equal(ScoringState.Unscored, (await harness.GetJobAsync(invalid.Id)).Scoring);
        Assert.Equal(ScoringState.Scored, (await harness.GetJobAsync(valid.Id)).Scoring);
    }

    [Fact]
    public async Task ImportAsync_WithAScoreForAnUnknownJob_RejectsThatLine()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.WriteExchangeFileAsync(ExchangeFiles.Scored, ScoreLine(Guid.CreateVersion7()));

        ExchangeImportResult result = await harness.Importer.ImportAsync();

        Assert.Equal(0, result.ScoresImported);
        Assert.Contains("no job is stored", Assert.Single(result.Rejections).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_WithAJobIdThatIsNotAnIdentifier_RejectsThatLine()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.WriteExchangeFileAsync(ExchangeFiles.Scored, ScoreLine(Guid.CreateVersion7(), payload => payload["job_id"] = "job-42"));

        ExchangeImportResult result = await harness.Importer.ImportAsync();

        Assert.Contains("not an identifier", Assert.Single(result.Rejections).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_WithAValidKitLine_AttachesTheKitToTheApplication()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = LlmTestJobs.NewPursuedJob();
        await harness.SaveAsync(job);
        await harness.SaveAsync(Application.Create(job.Id, ApplicationStatus.Saved, LlmTestJobs.SeenAt, "Pursued from the inbox."));
        await harness.WriteExchangeFileAsync(ExchangeFiles.Kits, KitLine(job.Id));

        ExchangeImportResult result = await harness.Importer.ImportAsync();

        Assert.Equal(1, result.KitsImported);
        Application stored = await harness.GetApplicationAsync(job.Id);
        Assert.Equal(KitState.Ready, stored.KitState);
        Assert.NotNull(stored.Kit);
        Assert.Equal("claude-code", stored.Kit.Model);
        Assert.Equal(3, stored.Kit.FitSummary.Count);
        Assert.Equal(6, stored.Kit.AtsAnswers.Count);
        Assert.Empty(stored.Kit.LintIssues);
    }

    [Fact]
    public async Task ImportAsync_WithAKitThatBreaksTheVoiceRules_KeepsTheTextAndRecordsTheIssues()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = LlmTestJobs.NewPursuedJob();
        await harness.SaveAsync(job);
        await harness.SaveAsync(Application.Create(job.Id, ApplicationStatus.Saved, LlmTestJobs.SeenAt, "Pursued from the inbox."));
        await harness.WriteExchangeFileAsync(ExchangeFiles.Kits, KitLine(job.Id, payload => payload["cover_note"] = "This is the role I want!"));

        ExchangeImportResult result = await harness.Importer.ImportAsync();

        Assert.Equal(1, result.KitsImported);
        Application stored = await harness.GetApplicationAsync(job.Id);
        Assert.Equal(KitState.Failed, stored.KitState);
        Assert.NotNull(stored.Kit);
        Assert.Equal("This is the role I want!", stored.Kit.CoverNote);
        Assert.Contains("exclamation mark", Assert.Single(stored.Kit.LintIssues), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_WithAKitForAJobWithoutAnApplication_RejectsThatLine()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = LlmTestJobs.NewPursuedJob();
        await harness.SaveAsync(job);
        await harness.WriteExchangeFileAsync(ExchangeFiles.Kits, KitLine(job.Id));

        ExchangeImportResult result = await harness.Importer.ImportAsync();

        Assert.Equal(0, result.KitsImported);
        Assert.Contains("no application is open", Assert.Single(result.Rejections).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_WithAKitInAnotherLanguage_RejectsThatLine()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = LlmTestJobs.NewPursuedJob();
        await harness.SaveAsync(job);
        await harness.SaveAsync(Application.Create(job.Id, ApplicationStatus.Saved, LlmTestJobs.SeenAt, "Pursued from the inbox."));
        await harness.WriteExchangeFileAsync(ExchangeFiles.Kits, KitLine(job.Id, payload => payload["language"] = "de"));

        ExchangeImportResult result = await harness.Importer.ImportAsync();

        Assert.Equal(0, result.KitsImported);
        Assert.Contains("is not en", Assert.Single(result.Rejections).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_WithoutAnyResultFiles_ImportsNothingAndRejectsNothing()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();

        ExchangeImportResult result = await harness.Importer.ImportAsync();

        Assert.Equal(0, result.ScoresImported);
        Assert.Equal(0, result.KitsImported);
        Assert.Empty(result.Rejections);
    }

    [Fact]
    public async Task ImportAsync_AfterAnExport_StoresTheJobTheExportWroteOut()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = LlmTestJobs.NewUnscoredJob();
        await harness.SaveAsync(job);
        await harness.Exporter.ExportAsync();

        string exported = Assert.Single(await harness.ReadExchangeLinesAsync(ExchangeFiles.ToScore));
        string jobId = JsonDocument.Parse(exported).RootElement.GetProperty("job_id").GetString()!;
        await harness.WriteExchangeFileAsync(ExchangeFiles.Scored, ScoreLine(Guid.Parse(jobId)));

        ExchangeImportResult result = await harness.Importer.ImportAsync();

        Assert.Equal(1, result.ScoresImported);
        Assert.Equal(ScoringState.Scored, (await harness.GetJobAsync(job.Id)).Scoring);
    }

    [Fact]
    public async Task ImportAsync_AfterEveryLineImports_RenamesTheScoredFileSoALaterImportCannotReapplyIt()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = LlmTestJobs.NewUnscoredJob();
        await harness.SaveAsync(job);
        await harness.WriteExchangeFileAsync(ExchangeFiles.Scored, ScoreLine(job.Id));

        ExchangeImportResult result = await harness.Importer.ImportAsync();

        Assert.Equal(1, result.ScoresImported);
        Assert.False(File.Exists(harness.ExchangeFile(ExchangeFiles.Scored)));
        string renamed = Assert.Single(Directory.GetFiles(harness.Paths.Exchange, "scored.imported-*.jsonl"));
        Assert.Contains(job.Id.ToString(), await File.ReadAllTextAsync(renamed), StringComparison.Ordinal);
        DateTimeOffset scoredAtAfterFirstImport = (await harness.GetJobAsync(job.Id)).Score!.ScoredAt;

        ExchangeImportResult second = await harness.Importer.ImportAsync();

        Assert.Equal(0, second.ScoresImported);
        Assert.Empty(second.Rejections);
        Assert.Equal(scoredAtAfterFirstImport, (await harness.GetJobAsync(job.Id)).Score!.ScoredAt);
    }

    [Fact]
    public async Task ImportAsync_WithSomeLinesRejected_KeepsOnlyTheRejectedLinesUnderTheOriginalFileName()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job rejected = LlmTestJobs.NewUnscoredJob("Rejected Labs");
        Job accepted = LlmTestJobs.NewUnscoredJob("Accepted Labs");
        await harness.SaveAsync(rejected, accepted);
        string badLine = ScoreLine(rejected.Id, payload => payload["facts"]!["employment_type"] = "freelance");
        await harness.WriteExchangeFileAsync(ExchangeFiles.Scored, badLine, ScoreLine(accepted.Id));

        ExchangeImportResult result = await harness.Importer.ImportAsync();

        Assert.Equal(1, result.ScoresImported);
        Assert.Single(result.Rejections);
        Assert.True(File.Exists(harness.ExchangeFile(ExchangeFiles.Scored)));
        Assert.Empty(Directory.GetFiles(harness.Paths.Exchange, "scored.imported-*.jsonl"));
        Assert.Equal(badLine, (await File.ReadAllTextAsync(harness.ExchangeFile(ExchangeFiles.Scored))).Trim());

        ExchangeImportResult retry = await harness.Importer.ImportAsync();

        Assert.Equal(0, retry.ScoresImported);
        ExchangeLineRejection retryRejection = Assert.Single(retry.Rejections);
        Assert.Contains("employment_type", retryRejection.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportAsync_AfterEveryKitLineImports_RenamesTheKitsFileSoALaterImportCannotReattachIt()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = LlmTestJobs.NewPursuedJob();
        await harness.SaveAsync(job);
        await harness.SaveAsync(Application.Create(job.Id, ApplicationStatus.Saved, LlmTestJobs.SeenAt, "Pursued from the inbox."));
        await harness.WriteExchangeFileAsync(ExchangeFiles.Kits, KitLine(job.Id));

        ExchangeImportResult result = await harness.Importer.ImportAsync();

        Assert.Equal(1, result.KitsImported);
        Assert.False(File.Exists(harness.ExchangeFile(ExchangeFiles.Kits)));
        Assert.Single(Directory.GetFiles(harness.Paths.Exchange, "kits.imported-*.jsonl"));

        ExchangeImportResult second = await harness.Importer.ImportAsync();

        Assert.Equal(0, second.KitsImported);
        Assert.Empty(second.Rejections);
    }

    private static string ScoreLine(Guid jobId, Action<JsonObject>? change = null)
    {
        return Line(LlmFixtures.Read(LlmFixtures.ScorePayloadFile), jobId, change);
    }

    private static string KitLine(Guid jobId, Action<JsonObject>? change = null)
    {
        return Line(LlmFixtures.Read(LlmFixtures.KitPayloadFile), jobId, change);
    }

    private static string Line(string fixture, Guid jobId, Action<JsonObject>? change)
    {
        JsonObject payload = JsonNode.Parse(fixture)!.AsObject();
        payload["job_id"] = jobId.ToString();
        change?.Invoke(payload);

        return payload.ToJsonString();
    }
}
