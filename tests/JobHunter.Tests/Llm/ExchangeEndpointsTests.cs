using System.Text;
using System.Text.Json.Nodes;
using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Llm.Exchange;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Tests.Llm;

/// <summary>Proves that the exchange endpoints hand out the lines the export writes and store scores and new jobs through the same import the Import button runs.</summary>
public sealed class ExchangeEndpointsTests
{
    private const string PostingLink = "https://careers.example.com/jobs/7781";

    [Fact]
    public async Task GetJobsToScoreAsync_WithAnUnscoredJob_ReturnsTheLinesTheExportWrites()
    {
        await using LlmTestHarness harness = new();
        await harness.InitializeAsync();
        await harness.SaveAsync(LlmTestJobs.NewUnscoredJob(), LlmTestJobs.NewPursuedJob());
        await harness.Exporter.ExportAsync();

        ContentHttpResult result = await ExchangeEndpoints.GetJobsToScoreAsync(harness.Exporter, CancellationToken.None);

        Assert.Equal(await File.ReadAllTextAsync(harness.ExchangeFile(ExchangeFiles.ToScore)), result.ResponseContent);
        Assert.StartsWith("application/x-ndjson", result.ContentType, StringComparison.Ordinal);
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

    private static HttpRequest Request(string body)
    {
        DefaultHttpContext context = new();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));

        return context.Request;
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
