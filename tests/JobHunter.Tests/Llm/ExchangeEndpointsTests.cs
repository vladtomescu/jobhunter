using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Llm;
using JobHunter.Llm.Exchange;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Tests.Llm;

/// <summary>Proves that the exchange endpoints hand out the lines the export writes and store scores and new jobs through the same import the Import button runs, hand out and store cover letters through the check the Write cover letter button uses, and hand out everything an interview prep is written from.</summary>
public sealed class ExchangeEndpointsTests
{
    private const string PostingLink = "https://careers.example.com/jobs/7781";

    [Fact]
    public async Task GetJobsToScoreAsync_WithoutJobIds_ReturnsTheLinesTheExportWrites()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job unscored = LlmTestJobs.NewUnscoredJob();
        await harness.SaveAsync(unscored, LlmTestJobs.NewPursuedJob());
        await harness.Exporter.ExportAsync();

        ContentHttpResult result = await ExchangeEndpoints.GetJobsToScoreAsync(harness.Exporter, [], CancellationToken.None);

        Assert.Equal(await File.ReadAllTextAsync(harness.ExchangeFile(ExchangeFiles.ToScore)), result.ResponseContent);
        Assert.Equal<Guid>([unscored.Id], JobIds(result));
        Assert.StartsWith("application/x-ndjson", result.ContentType, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetJobsToScoreAsync_WithTheIdsOfAScoredAndADroppedInactiveJob_ReturnsTheLinesOfBoth()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job scored = LlmTestJobs.NewPursuedJob();
        Job dropped = LlmTestJobs.NewUnscoredJob("Contoso Analytics");
        dropped.Drop("Aged out of the inbox.");
        dropped.MissRun();
        dropped.MissRun();
        await harness.SaveAsync(scored, dropped, LlmTestJobs.NewUnscoredJob("Tailspin Toys"));

        ContentHttpResult result = await ExchangeEndpoints.GetJobsToScoreAsync(harness.Exporter, [scored.Id.ToString(), dropped.Id.ToString()], CancellationToken.None);

        Assert.False(dropped.IsActive);
        Assert.Equal<Guid>([.. new[] { scored.Id, dropped.Id }.Order()], [.. JobIds(result).Order()]);
        Assert.Contains($"\"title\":\"{scored.Title}\"", result.ResponseContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetJobsToScoreAsync_WithAnIdThatIsNotStored_LeavesItOut()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = LlmTestJobs.NewUnscoredJob();
        await harness.SaveAsync(job);

        ContentHttpResult result = await ExchangeEndpoints.GetJobsToScoreAsync(harness.Exporter, [Guid.NewGuid().ToString(), job.Id.ToString(), "not-an-id"], CancellationToken.None);

        Assert.Equal<Guid>([job.Id], JobIds(result));
    }

    [Fact]
    public async Task ImportScoredAsync_WithAScoredPursuedJob_ReplacesTheScoreAndClassAndKeepsTriageAndApplication()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = LlmTestJobs.NewUnscoredJob();
        job.RecordScore(LlmTestJobs.NewScoreCard(), JobClass.C);
        job.Pursue(LlmTestJobs.SeenAt);
        await harness.SaveAsync(job);
        Application application = Application.Create(job.Id, ApplicationStatus.Saved, LlmTestJobs.SeenAt, "Pursued from the inbox.");
        application.AttachKit(new ApplicationKit(["A fact."], "A cover note.", ["A question?"], "resume.pdf", "en", LlmTestJobs.SeenAt, "claude-opus-5", []));
        await harness.SaveAsync(application);

        Ok<ScoredLinesResponse> result = await ExchangeEndpoints.ImportScoredAsync(Request(ScoreLine(job.Id.ToString()) + "\n"), harness.Importer, CancellationToken.None);

        Job stored = await harness.GetJobAsync(job.Id);
        Application kept = await harness.GetApplicationAsync(job.Id);
        Assert.Equal(1, result.Value!.Imported);
        Assert.Equal(ExchangeFiles.Model, stored.Score!.Model);
        Assert.NotEqual(job.Score!.Reasoning, stored.Score.Reasoning);
        Assert.NotEqual(job.Class, stored.Class);
        Assert.Equal(stored.Class.ToString(), result.Value.Lines[0].Class);
        Assert.Equal(TriageState.Pursued, stored.Triage);
        Assert.Equal(job.TriagedAt, stored.TriagedAt);
        Assert.Equal(application.Status, kept.Status);
        Assert.Equal(application.StatusChangedAt, kept.StatusChangedAt);
        Assert.Equal(application.KitState, kept.KitState);
        Assert.Equal(application.Kit!.CoverNote, kept.Kit!.CoverNote);
    }

    [Fact]
    public async Task ImportScoredAsync_WithAValidAndAMalformedLine_StoresTheValidOneAndReportsBoth()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = LlmTestJobs.NewUnscoredJob();
        await harness.SaveAsync(job);

        Ok<ScoredLinesResponse> result = await ExchangeEndpoints.ImportScoredAsync(Request("{ not json at all\n" + ScoreLine(job.Id.ToString()) + "\n"), harness.Importer, CancellationToken.None);

        Job stored = await harness.GetJobAsync(job.Id);
        ScoredLinesResponse response = result.Value!;
        Assert.Equal(1, response.Imported);
        Assert.Equal(1, response.Refused);
        Assert.Contains("does not match the score schema", response.Lines[0].Refusal, StringComparison.Ordinal);
        Assert.Equal(job.Id.ToString(), response.Lines[1].JobId);
        Assert.Equal(stored.Class.ToString(), response.Lines[1].Class);
        Assert.Equal(stored.Score!.Total, response.Lines[1].Total);
        Assert.Equal<string>([.. stored.Flags.Select(flag => flag.ToString())], response.Lines[1].Flags);
        Assert.Equal(ExchangeFiles.Model, stored.Score.Model);
    }

    [Fact]
    public async Task ImportNewJobAsync_WithAValidLine_StoresTheManualJobWithTheClassTheAppComputed()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();

        Results<Created<NewJobResponse>, Ok<NewJobResponse>, BadRequest<NewJobResponse>> result = await ExchangeEndpoints.ImportNewJobAsync(Request(NewJobLine()), harness.NewJobs, CancellationToken.None);

        Created<NewJobResponse> created = Assert.IsType<Created<NewJobResponse>>(result.Result);
        NewJobResponse response = created.Value!;
        Job stored = await harness.GetJobAsync(response.JobId!.Value);
        Assert.False(response.AlreadyExists);
        Assert.Null(response.Refusal);
        Assert.True(stored.IsManual);
        Assert.Equal(ScoringState.Scored, stored.Scoring);
        Assert.Equal(stored.Class.ToString(), response.Class);
        Assert.Equal(stored.Score!.Total, response.Total);
        Assert.Equal($"/jobs/{stored.Id}", response.JobPage);
        Assert.Equal(response.JobPage, created.Location);
        Assert.Equal("Staff Platform Engineer", stored.Title);
    }

    [Fact]
    public async Task ImportNewJobAsync_WithALinkAlreadyHeld_StoresNothingAndReportsTheExistingJob()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        NewJobOutcome first = await harness.NewJobs.ImportAsync(NewJobLine());

        Results<Created<NewJobResponse>, Ok<NewJobResponse>, BadRequest<NewJobResponse>> result = await ExchangeEndpoints.ImportNewJobAsync(
            Request(NewJobLine(line => line["apply_url"] = PostingLink + "?utm_source=feed")),
            harness.NewJobs,
            CancellationToken.None);

        NewJobResponse response = Assert.IsType<Ok<NewJobResponse>>(result.Result).Value!;
        Assert.True(response.AlreadyExists);
        Assert.Equal(first.JobId, response.JobId);
        Assert.Equal(first.Class.ToString(), response.Class);
        Assert.Equal(1, await CountJobsAsync(harness));
    }

    [Fact]
    public async Task ImportNewJobAsync_WithAnUnknownProperty_RefusesTheLineAndStoresNothing()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();

        Results<Created<NewJobResponse>, Ok<NewJobResponse>, BadRequest<NewJobResponse>> result = await ExchangeEndpoints.ImportNewJobAsync(
            Request(NewJobLine(line => line["class"] = "A")),
            harness.NewJobs,
            CancellationToken.None);

        NewJobResponse response = Assert.IsType<BadRequest<NewJobResponse>>(result.Result).Value!;
        Assert.Null(response.JobId);
        Assert.Contains("does not match the new job schema", response.Refusal, StringComparison.Ordinal);
        Assert.Equal(0, await CountJobsAsync(harness));
    }

    [Fact]
    public async Task ImportAsync_WithAnUnknownPropertyInsideTheScore_RefusesTheLine()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();

        NewJobOutcome outcome = await harness.NewJobs.ImportAsync(NewJobLine(line => line["score"]!["class"] = "A"));

        Assert.Contains("does not match the new job schema", outcome.Refusal, StringComparison.Ordinal);
        Assert.Equal(0, await CountJobsAsync(harness));
    }

    [Theory]
    [InlineData("apply_url", "not a link", "apply_url")]
    [InlineData("apply_url", "ftp://files.example.com/job", "apply_url")]
    [InlineData("title", " ", "title is empty")]
    [InlineData("company", "", "company is empty")]
    [InlineData("description", "", "description is empty")]
    public async Task ImportAsync_WithAnInvalidJobField_RefusesTheLineAndStoresNothing(string property, string value, string expectedReason)
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();

        NewJobOutcome outcome = await harness.NewJobs.ImportAsync(NewJobLine(line => line[property] = value));

        Assert.Null(outcome.JobId);
        Assert.Contains(expectedReason, outcome.Refusal, StringComparison.Ordinal);
        Assert.Equal(0, await CountJobsAsync(harness));
    }

    [Fact]
    public async Task ImportAsync_WithAScoreThatNamesAJobId_RefusesTheLineAndStoresNothing()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();

        NewJobOutcome outcome = await harness.NewJobs.ImportAsync(NewJobLine(line => line["score"]!["job_id"] = Guid.NewGuid().ToString()));

        Assert.Contains("score.job_id must be empty", outcome.Refusal, StringComparison.Ordinal);
        Assert.Equal(0, await CountJobsAsync(harness));
    }

    [Fact]
    public async Task ImportAsync_WithAScoreOutsideTheVocabulary_RefusesTheLineAndStoresNothing()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();

        NewJobOutcome outcome = await harness.NewJobs.ImportAsync(NewJobLine(line => line["score"]!["facts"]!["employment_type"] = "freelance"));

        Assert.Contains("employment_type", outcome.Refusal, StringComparison.Ordinal);
        Assert.Equal(0, await CountJobsAsync(harness));
    }

    [Fact]
    public async Task GetJobToCoverAsync_WithASavedScoredJob_ReturnsTheKitInputWithTheLanguageHintAndTheResume()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = await SaveSavedJobAsync(harness);
        await File.WriteAllTextAsync(harness.ResumeMarkdownPath, "# Resume\n\nLedger service owner.");

        Results<Ok<CoverLetterExchangeInput>, NotFound<ExchangeRefusal>, Conflict<ExchangeRefusal>> result = await ExchangeEndpoints.GetJobToCoverAsync(harness.CoverLetters, harness.Settings, job.Id.ToString(), CancellationToken.None);

        CoverLetterExchangeInput input = Assert.IsType<Ok<CoverLetterExchangeInput>>(result.Result).Value!;
        Assert.Equal(job.Id.ToString(), input.JobId);
        Assert.Equal(job.Title, input.Title);
        Assert.Equal(job.Company, input.Company);
        Assert.Equal("https://jobs.example.com/apply", input.ApplyUrl);
        Assert.Equal("A", input.Class);
        Assert.Equal("en", input.LanguageHint);
        Assert.Equal<string>(["timezone"], input.Score.BlockingUnknowns);
        Assert.Equal("# Resume\n\nLedger service owner.", input.Resume);
        JsonObject written = JsonNode.Parse(JsonSerializer.Serialize(input))!.AsObject();
        Assert.Equal<string>(
            ["job_id", "title", "company", "apply_url", "description", "score", "class", "flags", "language_hint", "resume"],
            [.. written.Select(property => property.Key)]);
    }

    [Fact]
    public async Task GetJobToCoverAsync_WithoutAReadableResume_ReturnsTheInputWithANullResume()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = await SaveSavedJobAsync(harness);

        Results<Ok<CoverLetterExchangeInput>, NotFound<ExchangeRefusal>, Conflict<ExchangeRefusal>> result = await ExchangeEndpoints.GetJobToCoverAsync(harness.CoverLetters, harness.Settings, job.Id.ToString(), CancellationToken.None);

        CoverLetterExchangeInput input = Assert.IsType<Ok<CoverLetterExchangeInput>>(result.Result).Value!;
        Assert.Null(input.Resume);
        Assert.Contains("\"resume\":null", JsonSerializer.Serialize(input), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-an-id")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("0f8fad5b-d9cb-469f-a165-70867728950e")]
    public async Task GetJobToCoverAsync_WithAMalformedOrUnknownId_ReturnsNotFoundWithTheReason(string? jobId)
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();

        Results<Ok<CoverLetterExchangeInput>, NotFound<ExchangeRefusal>, Conflict<ExchangeRefusal>> result = await ExchangeEndpoints.GetJobToCoverAsync(harness.CoverLetters, harness.Settings, jobId, CancellationToken.None);

        ExchangeRefusal refusal = Assert.IsType<NotFound<ExchangeRefusal>>(result.Result).Value!;
        Assert.False(string.IsNullOrWhiteSpace(refusal.Refusal));
    }

    [Fact]
    public async Task GetJobToCoverAsync_WithAJobThatIsNotSaved_ReturnsConflictWithTheReason()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = LlmTestJobs.NewUnscoredJob();
        job.RecordScore(LlmTestJobs.NewScoreCard(), JobClass.A);
        await harness.SaveAsync(job);

        Results<Ok<CoverLetterExchangeInput>, NotFound<ExchangeRefusal>, Conflict<ExchangeRefusal>> result = await ExchangeEndpoints.GetJobToCoverAsync(harness.CoverLetters, harness.Settings, job.Id.ToString(), CancellationToken.None);

        ExchangeRefusal refusal = Assert.IsType<Conflict<ExchangeRefusal>>(result.Result).Value!;
        Assert.Contains("is not saved", refusal.Refusal, StringComparison.Ordinal);
        Assert.Equal("{\"refusal\":\"" + refusal.Refusal + "\"}", JsonSerializer.Serialize(refusal));
    }

    [Fact]
    public async Task ImportCoverLetterAsync_WithAValidLetter_StoresItAndAnswersCreatedWithThePageAndNoLintIssues()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = await SaveSavedJobAsync(harness);

        Results<Created<CoverLetterResponse>, BadRequest<CoverLetterResponse>> result = await ExchangeEndpoints.ImportCoverLetterAsync(Request(CoverLetterLine(job.Id)), harness.CoverLetters, CancellationToken.None);

        Created<CoverLetterResponse> created = Assert.IsType<Created<CoverLetterResponse>>(result.Result);
        CoverLetterResponse response = created.Value!;
        Assert.Equal(job.Id, response.JobId);
        Assert.Equal($"/jobs/{job.Id}", response.JobPage);
        Assert.Equal(response.JobPage, created.Location);
        Assert.Empty(response.LintIssues);
        Assert.Null(response.Refusal);
        ApplicationCoverLetter stored = (await harness.GetApplicationAsync(job.Id)).CoverLetter!;
        Assert.Equal(ExchangeFiles.Model, stored.Model);
        Assert.Equal(4, stored.Paragraphs.Count);
    }

    [Fact]
    public async Task ImportCoverLetterAsync_WithALetterThatBreaksTheVoiceRules_StoresItAndReportsTheIssues()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = await SaveSavedJobAsync(harness);

        Results<Created<CoverLetterResponse>, BadRequest<CoverLetterResponse>> result = await ExchangeEndpoints.ImportCoverLetterAsync(
            Request(CoverLetterLine(job.Id, letter => letter["closing"] = "Speak soon!")),
            harness.CoverLetters,
            CancellationToken.None);

        CoverLetterResponse response = Assert.IsType<Created<CoverLetterResponse>>(result.Result).Value!;
        Assert.StartsWith("closing: exclamation mark", Assert.Single(response.LintIssues), StringComparison.Ordinal);
        Assert.Equal(response.LintIssues, (await harness.GetApplicationAsync(job.Id)).CoverLetter!.LintIssues);
    }

    [Fact]
    public async Task ImportCoverLetterAsync_WithAnUnknownProperty_AnswersBadRequestAndStoresNothing()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = await SaveSavedJobAsync(harness);

        Results<Created<CoverLetterResponse>, BadRequest<CoverLetterResponse>> result = await ExchangeEndpoints.ImportCoverLetterAsync(
            Request(CoverLetterLine(job.Id, letter => letter["signature"] = "A name.")),
            harness.CoverLetters,
            CancellationToken.None);

        CoverLetterResponse response = Assert.IsType<BadRequest<CoverLetterResponse>>(result.Result).Value!;
        Assert.Null(response.JobId);
        Assert.Contains("does not match the cover letter schema", response.Refusal, StringComparison.Ordinal);
        Assert.Null((await harness.GetApplicationAsync(job.Id)).CoverLetter);
    }

    [Fact]
    public async Task ImportCoverLetterAsync_ForAJobThatIsNotSaved_AnswersBadRequestWithTheReason()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = LlmTestJobs.NewUnscoredJob();
        await harness.SaveAsync(job);

        Results<Created<CoverLetterResponse>, BadRequest<CoverLetterResponse>> result = await ExchangeEndpoints.ImportCoverLetterAsync(Request(CoverLetterLine(job.Id)), harness.CoverLetters, CancellationToken.None);

        CoverLetterResponse response = Assert.IsType<BadRequest<CoverLetterResponse>>(result.Result).Value!;
        Assert.Contains("is not saved", response.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportCoverLetterAsync_WithTwoParagraphs_AnswersBadRequestWithTheBounds()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = await SaveSavedJobAsync(harness);

        Results<Created<CoverLetterResponse>, BadRequest<CoverLetterResponse>> result = await ExchangeEndpoints.ImportCoverLetterAsync(
            Request(CoverLetterLine(job.Id, letter => letter["paragraphs"] = new JsonArray("One.", "Two."))),
            harness.CoverLetters,
            CancellationToken.None);

        CoverLetterResponse response = Assert.IsType<BadRequest<CoverLetterResponse>>(result.Result).Value!;
        Assert.Contains("3 to 7", response.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetJobToPrepAsync_ForAJobWithAnApplicationAKitAndALetter_ReturnsThePostingTheScoreTheResumeTheApplicationAndThePay()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = LlmTestJobs.NewPursuedJob();
        await harness.SaveAsync(job);
        await harness.SaveAsync(NewApplicationAtTheTechRound(job.Id));
        await File.WriteAllTextAsync(harness.ResumeMarkdownPath, "# Resume\n\nLedger service owner.");

        Results<Ok<PrepExchangeInput>, NotFound<ExchangeRefusal>> result = await ExchangeEndpoints.GetJobToPrepAsync(harness.Exporter, job.Id.ToString(), CancellationToken.None);

        PrepExchangeInput input = Assert.IsType<Ok<PrepExchangeInput>>(result.Result).Value!;
        Assert.Equal(job.Id.ToString(), input.JobId);
        Assert.Equal(job.Title, input.Title);
        Assert.Equal(job.Company, input.Company);
        Assert.Equal("Remote, Europe", input.Location);
        Assert.Equal("remote", input.RemoteHint);
        Assert.Equal("b2b", input.EmploymentHint);
        Assert.Equal("90000-120000 EUR per year", input.CompText);
        Assert.Equal("https://jobs.example.com/apply", input.ApplyUrl);
        Assert.Equal("2026-09-11T08:00:00.0000000+00:00", input.PostedAt);
        Assert.Equal("Plain text description.", input.Description);
        Assert.Equal("A", input.Class);
        Assert.Equal<string>(["timezone"], input.Score!.BlockingUnknowns);
        Assert.Equal<string>([.. job.Flags.Select(flag => flag.ToString())], input.Flags);
        Assert.Equal("# Resume\n\nLedger service owner.", input.Resume);

        PrepApplication application = input.Application!;
        Assert.Equal("Tech", application.Status);
        Assert.Equal("2026-09-23T08:00:00.0000000+00:00", application.StatusChangedAt);
        Assert.Equal("2026-09-15T08:00:00.0000000+00:00", application.AppliedAt);
        Assert.Equal("Ats", application.Channel);
        Assert.Equal<string>(["Saved", "Applied", "Screening", "Tech"], [.. application.History.Select(entry => entry.Status)]);
        Assert.Equal("Recruiter call booked.", application.History[2].Note);
        Assert.Equal("The recruiter said the team owns its on-call rotation.", Assert.Single(application.Notes).Text);
        Assert.Equal(new PrepContact("Jordan Example", "Talent partner", "https://people.example.com/jordan"), application.Contact);
        Assert.Equal("Prepare the tech round", application.NextAction);
        Assert.Equal("2026-09-25", application.NextActionDue);
        Assert.Equal("They asked for a range; I asked for theirs first.", application.CompDiscussed);
        Assert.Equal("A cover note.", application.Kit!.CoverNote);
        Assert.Equal("Open to discuss. [CONFIRM]", Assert.Single(application.Kit.AtsAnswers).Answer);
        Assert.Equal<string>(["One.", "Two.", "Three."], application.CoverLetter!.Paragraphs);
        Assert.Equal(ApplicationStatus.Tech, (await harness.GetApplicationAsync(job.Id)).Status);

        JsonObject written = JsonNode.Parse(JsonSerializer.Serialize(input))!.AsObject();
        JsonObject writtenApplication = written["application"]!.AsObject();
        JsonObject writtenKit = writtenApplication["kit"]!.AsObject();
        Assert.Equal<string>(
            ["job_id", "title", "company", "location", "remote_hint", "employment_hint", "comp_text", "apply_url", "posted_at", "description", "score", "class", "flags", "resume", "application", "pay"],
            PropertyNames(written));
        Assert.Equal<string>(
            ["status", "status_changed_at", "applied_at", "channel", "history", "notes", "contact", "next_action", "next_action_due", "comp_discussed", "kit", "cover_letter"],
            PropertyNames(writtenApplication));
        Assert.Equal<string>(["status", "at", "note"], PropertyNames(writtenApplication["history"]![0]!.AsObject()));
        Assert.Equal<string>(["at", "text"], PropertyNames(writtenApplication["notes"]![0]!.AsObject()));
        Assert.Equal<string>(["name", "role", "link"], PropertyNames(writtenApplication["contact"]!.AsObject()));
        Assert.Equal<string>(["fit_summary", "cover_note", "call_questions", "ats_answers", "language", "generated_at"], PropertyNames(writtenKit));
        Assert.Equal<string>(["question", "answer"], PropertyNames(writtenKit["ats_answers"]![0]!.AsObject()));
        Assert.Equal<string>(["language", "salutation", "paragraphs", "closing", "written_at"], PropertyNames(writtenApplication["cover_letter"]!.AsObject()));
        Assert.Equal<string>(
            ["currency", "min_employment_annual", "min_contractor_hourly", "target_annual", "contract_preference", "home_city", "home_country"],
            PropertyNames(written["pay"]!.AsObject()));
    }

    [Fact]
    public async Task GetJobToPrepAsync_ForAJobWithoutAnApplication_ReturnsANullApplicationAndANullResume()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = LlmTestJobs.NewPursuedJob();
        await harness.SaveAsync(job);

        Results<Ok<PrepExchangeInput>, NotFound<ExchangeRefusal>> result = await ExchangeEndpoints.GetJobToPrepAsync(harness.Exporter, job.Id.ToString(), CancellationToken.None);

        PrepExchangeInput input = Assert.IsType<Ok<PrepExchangeInput>>(result.Result).Value!;
        Assert.Null(input.Application);
        Assert.Null(input.Resume);
        Assert.Equal("A", input.Class);
        string written = JsonSerializer.Serialize(input);
        Assert.Contains("\"application\":null", written, StringComparison.Ordinal);
        Assert.Contains("\"resume\":null", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetJobToPrepAsync_ForAJobDroppedBeforeItWasScored_ReturnsANullScoreAndClass()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = LlmTestJobs.NewUnscoredJob();
        job.Drop("Aged out of the inbox.");
        await harness.SaveAsync(job);

        Results<Ok<PrepExchangeInput>, NotFound<ExchangeRefusal>> result = await ExchangeEndpoints.GetJobToPrepAsync(harness.Exporter, job.Id.ToString(), CancellationToken.None);

        PrepExchangeInput input = Assert.IsType<Ok<PrepExchangeInput>>(result.Result).Value!;
        Assert.Equal(JobClass.D, job.Class);
        Assert.Null(input.Score);
        Assert.Null(input.Class);
        Assert.Equal(job.Title, input.Title);
        string written = JsonSerializer.Serialize(input);
        Assert.Contains("\"score\":null", written, StringComparison.Ordinal);
        Assert.Contains("\"class\":null", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetJobToPrepAsync_ForAPostingLongerThanAScoringRequestCarries_ReturnsTheWholeDescription()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = LlmTestJobs.NewUnscoredJob();
        string description = string.Join(' ', Enumerable.Repeat("The team runs the ledger on PostgreSQL and Kafka.", 200));
        job.ReviseDescription(description, "hash-long");
        await harness.SaveAsync(job);

        Results<Ok<PrepExchangeInput>, NotFound<ExchangeRefusal>> result = await ExchangeEndpoints.GetJobToPrepAsync(harness.Exporter, job.Id.ToString(), CancellationToken.None);

        PrepExchangeInput input = Assert.IsType<Ok<PrepExchangeInput>>(result.Result).Value!;
        Assert.True(description.Length > LlmRequests.DescriptionLimit);
        Assert.Equal(description, input.Description);
    }

    [Fact]
    public async Task GetJobToPrepAsync_ReturnsThePayFromTheSettings()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.Settings.ApplyAsync(settings =>
        {
            settings.ConfigureCompensation(55m, 72_000m, 115_000m);
            settings.ConfigureCandidate("NL", "Utrecht", true, false, "en,nl", "EUR", "Java, Kotlin", ContractPreference.Employee, false, null, settings.TitleIncludeTerms, settings.TitleExcludeTerms);
        });
        Job job = LlmTestJobs.NewUnscoredJob();
        await harness.SaveAsync(job);

        Results<Ok<PrepExchangeInput>, NotFound<ExchangeRefusal>> result = await ExchangeEndpoints.GetJobToPrepAsync(harness.Exporter, job.Id.ToString(), CancellationToken.None);

        PrepPay pay = Assert.IsType<Ok<PrepExchangeInput>>(result.Result).Value!.Pay;
        Assert.Equal(new PrepPay("EUR", 72_000m, 55m, 115_000m, "Employee", "Utrecht", "NL"), pay);
    }

    [Fact]
    public async Task GetJobToPrepAsync_WithNoPaySettings_ReturnsNullsBesideTheCurrencyAndTheContractPreference()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        Job job = LlmTestJobs.NewUnscoredJob();
        await harness.SaveAsync(job);

        Results<Ok<PrepExchangeInput>, NotFound<ExchangeRefusal>> result = await ExchangeEndpoints.GetJobToPrepAsync(harness.Exporter, job.Id.ToString(), CancellationToken.None);

        PrepPay pay = Assert.IsType<Ok<PrepExchangeInput>>(result.Result).Value!.Pay;
        Assert.Equal(new PrepPay(JobHunter.Domain.Settings.DefaultBaseCurrency, null, null, null, "Either", null, null), pay);
    }

    [Fact]
    public async Task GetJobToPrepAsync_AfterTheBaseCurrencyChangedWithoutARecompute_NamesTheCurrencyTheAmountsAreStillCountedIn()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.Settings.ApplyAsync(settings =>
        {
            settings.ConfigureCompensation(55m, 72_000m, 115_000m);
            settings.ConfigureCandidate(null, string.Empty, true, true, "en", "USD", string.Empty, ContractPreference.Either, false, null, settings.TitleIncludeTerms, settings.TitleExcludeTerms);
        });
        Job job = LlmTestJobs.NewUnscoredJob();
        await harness.SaveAsync(job);

        Results<Ok<PrepExchangeInput>, NotFound<ExchangeRefusal>> result = await ExchangeEndpoints.GetJobToPrepAsync(harness.Exporter, job.Id.ToString(), CancellationToken.None);

        PrepPay pay = Assert.IsType<Ok<PrepExchangeInput>>(result.Result).Value!.Pay;
        Assert.Equal("EUR", pay.Currency);
        Assert.Equal(72_000m, pay.MinEmploymentAnnual);
    }

    [Fact]
    public async Task GetJobToPrepAsync_WithAnIdThatNamesNoStoredJob_ReturnsNotFoundWithTheReason()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(LlmTestJobs.NewPursuedJob());

        Results<Ok<PrepExchangeInput>, NotFound<ExchangeRefusal>> result = await ExchangeEndpoints.GetJobToPrepAsync(harness.Exporter, "0f8fad5b-d9cb-469f-a165-70867728950e", CancellationToken.None);

        ExchangeRefusal refusal = Assert.IsType<NotFound<ExchangeRefusal>>(result.Result).Value!;
        Assert.Equal("no job is stored under the identifier 0f8fad5b-d9cb-469f-a165-70867728950e", refusal.Refusal);
    }

    [Theory]
    [InlineData("not-an-id")]
    [InlineData("")]
    [InlineData(null)]
    public async Task GetJobToPrepAsync_WithAValueThatIsNotAJobIdentifier_ReturnsNotFoundWithTheReason(string? jobId)
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();

        Results<Ok<PrepExchangeInput>, NotFound<ExchangeRefusal>> result = await ExchangeEndpoints.GetJobToPrepAsync(harness.Exporter, jobId, CancellationToken.None);

        ExchangeRefusal refusal = Assert.IsType<NotFound<ExchangeRefusal>>(result.Result).Value!;
        Assert.EndsWith("is not a job identifier", refusal.Refusal, StringComparison.Ordinal);
    }

    private static Application NewApplicationAtTheTechRound(Guid jobId)
    {
        DateTimeOffset at = LlmTestJobs.SeenAt;
        Application application = Application.Create(jobId, ApplicationStatus.Saved, at, "Saved from the inbox.");
        application.MarkApplied(at.AddDays(1), ApplicationChannel.Ats, "resume.pdf", null);
        application.MoveTo(ApplicationStatus.Screening, at.AddDays(4), "Recruiter call booked.");
        application.MoveTo(ApplicationStatus.Tech, at.AddDays(9), null);
        application.AddNote("The recruiter said the team owns its on-call rotation.", at.AddDays(5));
        application.RecordContact("Jordan Example", "Talent partner", "https://people.example.com/jordan");
        application.PlanNextAction("Prepare the tech round", new DateOnly(2026, 9, 25));
        application.RecordCompDiscussed("They asked for a range; I asked for theirs first.");
        application.AttachKit(new ApplicationKit(["A fact."], "A cover note.", ["A question?"], "resume.pdf", "en", at, "claude-opus-5", []) { AtsAnswers = [new AtsAnswer("Notice period", "Open to discuss. [CONFIRM]")] });
        application.AttachCoverLetter(new ApplicationCoverLetter("en", "Dear Northwind Labs team,", ["One.", "Two.", "Three."], "Kind regards,", at.AddDays(2), "claude-code", []));

        return application;
    }

    private static List<string> PropertyNames(JsonObject written)
    {
        return [.. written.Select(property => property.Key)];
    }

    private static async Task<Job> SaveSavedJobAsync(LlmTestHarness harness)
    {
        Job job = LlmTestJobs.NewPursuedJob();
        await harness.SaveAsync(job);
        await harness.SaveAsync(Application.Create(job.Id, ApplicationStatus.Saved, LlmTestJobs.SeenAt, "Saved from the inbox."));

        return job;
    }

    private static string CoverLetterLine(Guid jobId, Action<JsonObject>? change = null)
    {
        JsonObject letter = JsonNode.Parse(LlmFixtures.Read(LlmFixtures.CoverLetterPayloadFile))!.AsObject();
        letter["job_id"] = jobId.ToString();
        change?.Invoke(letter);

        return letter.ToJsonString();
    }

    private static HttpRequest Request(string body)
    {
        DefaultHttpContext context = new();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        return context.Request;
    }

    private static List<Guid> JobIds(ContentHttpResult result)
    {
        string[] lines = result.ResponseContent!.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        return [.. lines.Select(line => Guid.Parse(JsonNode.Parse(line)!["job_id"]!.GetValue<string>()))];
    }

    private static string ScoreLine(string jobId)
    {
        JsonObject score = JsonNode.Parse(LlmFixtures.Read(LlmFixtures.ScorePayloadFile))!.AsObject();
        score["job_id"] = jobId;

        return score.ToJsonString();
    }

    private static string NewJobLine(Action<JsonObject>? change = null)
    {
        JsonObject line = new()
        {
            ["apply_url"] = PostingLink,
            ["title"] = "Staff Platform Engineer",
            ["company"] = "Fabrikam Systems",
            ["location"] = "Remote, Europe",
            ["comp_text"] = "",
            ["description"] = "We build the internal developer platform on .NET and run it for forty product teams.",
            ["score"] = JsonNode.Parse(ScoreLine(string.Empty))
        };
        change?.Invoke(line);

        return line.ToJsonString();
    }

    private static async Task<int> CountJobsAsync(LlmTestHarness harness)
    {
        await using JobHunterDbContext context = await harness.ContextFactory.CreateDbContextAsync();

        return await context.Jobs.CountAsync();
    }
}
