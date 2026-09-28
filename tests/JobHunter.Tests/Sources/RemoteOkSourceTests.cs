using System.Net;
using System.Text;
using System.Text.Json;
using JobHunter.Domain;
using JobHunter.Pipeline;
using JobHunter.Sources;
using JobHunter.Sources.RemoteOk;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Tests.Sources;

/// <summary>Proves the RemoteOK parser maps the fixture fields correctly and the source fetches, caches and never throws.</summary>
public sealed class RemoteOkSourceTests
{
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "remoteok-sample.json");

    [Fact]
    public void Parse_FromFixture_SkipsTheLegalNoticeAndReturnsThreeJobs()
    {
        IReadOnlyList<RawJob> jobs = RemoteOkParser.Parse(ReadFixture());

        Assert.Equal(3, jobs.Count);
    }

    [Fact]
    public void Parse_FromFixture_MapsAJobWithStatedComp()
    {
        RawJob job = FindJob("1136796");

        Assert.Equal(JobSourceKind.RemoteOk, job.Source);
        Assert.Equal("Staff Software Development Engineer SDM", job.Title);
        Assert.Equal("Delinea", job.Company);
        Assert.Equal("https://remoteOK.com/remote-jobs/remote-staff-software-development-engineer-sdm-delinea-1136796", job.PostingUrl);
        Assert.Equal(job.PostingUrl, job.ApplyUrl);
        Assert.Null(job.Ats);
        Assert.Equal(150_000m, job.CompMin);
        Assert.Equal(185_000m, job.CompMax);
        Assert.Equal("USD", job.CompCurrency);
        Assert.Equal(CompPeriod.Year, job.CompPeriod);
        Assert.Equal(new DateTimeOffset(2026, 8, 16, 0, 0, 10, TimeSpan.Zero), job.PostedAt);
        Assert.Contains("backend", job.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.StartsWith("<p style=\"min-height:1.5em\">", job.DescriptionRaw, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_FromFixture_TreatsZeroSalaryAsUnstated()
    {
        RawJob job = FindJob("1137114");

        Assert.Null(job.CompMin);
        Assert.Null(job.CompMax);
        Assert.Null(job.CompCurrency);
        Assert.Null(job.CompPeriod);
    }

    [Fact]
    public void Parse_FromFixture_LeavesLocationTextAndCountryIsoAndRegionAsSourceGivesThem()
    {
        RawJob job = FindJob("1136447");

        Assert.Equal("Remote - US", job.LocationText);
        Assert.Null(job.CountryIso);
        Assert.Null(job.RegionText);
    }

    [Fact]
    public void Parse_WithNoDateField_FallsBackToEpoch()
    {
        string json = """
        [
            {"last_updated": 1, "legal": "notice"},
            {"id": "1", "slug": "a", "position": "Backend Engineer", "company": "Acme", "url": "https://remoteok.com/remote-jobs/a", "apply_url": "https://remoteok.com/remote-jobs/a", "description": "<p>Hi</p>", "tags": ["backend"], "epoch": 1700000000}
        ]
        """;

        IReadOnlyList<RawJob> jobs = RemoteOkParser.Parse(json);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), Assert.Single(jobs).PostedAt);
    }

    [Fact]
    public void Parse_WithNeitherDateNorEpoch_ReturnsNullPostedAt()
    {
        string json = """
        [
            {"last_updated": 1, "legal": "notice"},
            {"id": "1", "slug": "a", "position": "Backend Engineer", "company": "Acme", "url": "https://remoteok.com/remote-jobs/a", "description": "<p>Hi</p>", "tags": []}
        ]
        """;

        IReadOnlyList<RawJob> jobs = RemoteOkParser.Parse(json);

        Assert.Null(Assert.Single(jobs).PostedAt);
    }

    [Theory]
    [InlineData("<br/><br/>", "**NORTHERLY**", "RMTkyLjAuMi4x")]
    [InlineData("<br/><br/>", "NORTHERLY", "RMTk4LjUxLjEwMC43")]
    [InlineData("\n\n", "**STEADFAST**", "RMjAzLjAuMTEzLjk=")]
    public void Parse_WithTheAntiSpamNote_StripsTheNoteAndKeepsTheRestOfTheDescription(string separator, string word, string tag)
    {
        const string posting = "<p>We run a distributed platform on .NET.</p><p>Apply with a short note.</p>";
        string description = $"{posting}{separator}Please mention the word {word} and tag {tag} when applying to show you read the job post completely (#{tag}). This is a beta feature to avoid spam applicants. Companies can search these words to find applicants that read this and see they're human.";

        RawJob job = ParseOnePosting(description);

        Assert.Equal(posting, job.DescriptionRaw);
    }

    [Fact]
    public void Parse_WithoutTheAntiSpamNote_LeavesTheDescriptionAsItIs()
    {
        const string description = "<p>We run a distributed platform on .NET.</p><br/><br/><p>Please mention your notice period when you apply.</p>";

        RawJob job = ParseOnePosting(description);

        Assert.Equal(description, job.DescriptionRaw);
    }

    [Fact]
    public void Parse_FromFixture_StripsTheAntiSpamNoteFromEveryDescription()
    {
        IReadOnlyList<RawJob> jobs = RemoteOkParser.Parse(ReadFixture());

        Assert.All(jobs, job => Assert.DoesNotContain("mention the word", job.DescriptionRaw, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Kind_And_IsFullSnapshot_MatchTheSourceContract()
    {
        RemoteOkSource source = new(new HttpClient());

        Assert.Equal(JobSourceKind.RemoteOk, source.Kind);
        Assert.False(source.IsFullSnapshot);
    }

    [Fact]
    public void AddRemoteOkSource_Registers_AResolvableJobSource()
    {
        ServiceCollection services = new();
        services.AddRemoteOkSource();
        using ServiceProvider provider = services.BuildServiceProvider();

        IJobSource resolved = provider.GetRequiredService<IJobSource>();

        Assert.IsType<RemoteOkSource>(resolved);
    }

    [Fact]
    public async Task FetchAsync_WithASuccessfulResponse_CachesTheRawResponseAndReturnsParsedJobs()
    {
        string fixtureText = ReadFixture();
        FakeHttpMessageHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(fixtureText, Encoding.UTF8, "application/json")
        });
        using HttpClient httpClient = new(handler);
        RemoteOkSource source = new(httpClient);
        string cacheFolder = CreateTempCacheFolder();

        try
        {
            JobHunter.Domain.Settings settings = JobHunter.Domain.Settings.CreateDefault();
            settings.ConfigureContact("Test", "User", "test@example.com", string.Empty, string.Empty, string.Empty);
            SourceFetchContext context = new(DateTimeOffset.UtcNow.AddDays(-21), CandidateProfile.FromSettings(settings), cacheFolder, settings);

            SourceFetchResult result = await source.FetchAsync(context, CancellationToken.None);

            Assert.Null(result.Error);
            Assert.Equal(3, result.FetchedCount);
            Assert.Equal(3, result.Jobs.Count);
            string cachedFile = Assert.Single(Directory.GetFiles(cacheFolder));
            Assert.Equal(fixtureText, await File.ReadAllTextAsync(cachedFile));
            Assert.NotNull(handler.LastRequest);
            string userAgent = handler.LastRequest!.Headers.UserAgent.ToString();
            Assert.StartsWith("JobHunter/1.0 (+mailto:", userAgent, StringComparison.Ordinal);
            Assert.Contains("test@example.com", userAgent, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(cacheFolder, recursive: true);
        }
    }

    [Fact]
    public async Task FetchAsync_WhenTheCacheFolderDoesNotExistYet_CreatesItAndStillReturnsTheJobs()
    {
        FakeHttpMessageHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ReadFixture(), Encoding.UTF8, "application/json")
        });
        using HttpClient httpClient = new(handler);
        RemoteOkSource source = new(httpClient);
        string cacheRoot = Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"));
        string cacheFolder = Path.Combine(cacheRoot, "remoteok", "2026-09-15");

        try
        {
            SourceFetchContext context = new(DateTimeOffset.UtcNow.AddDays(-21), CandidateProfile.FromSettings(JobHunter.Domain.Settings.CreateDefault()), cacheFolder, JobHunter.Domain.Settings.CreateDefault());

            SourceFetchResult result = await source.FetchAsync(context, CancellationToken.None);

            Assert.Null(result.Error);
            Assert.Equal(3, result.Jobs.Count);
            Assert.True(Directory.Exists(cacheFolder));
            Assert.Single(Directory.GetFiles(cacheFolder));
        }
        finally
        {
            if (Directory.Exists(cacheRoot))
            {
                Directory.Delete(cacheRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task FetchAsync_WithAnEmptySettingsEmail_UsesTheNeutralUserAgent()
    {
        FakeHttpMessageHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ReadFixture(), Encoding.UTF8, "application/json")
        });
        using HttpClient httpClient = new(handler);
        RemoteOkSource source = new(httpClient);
        string cacheFolder = CreateTempCacheFolder();

        try
        {
            SourceFetchContext context = new(DateTimeOffset.UtcNow.AddDays(-21), CandidateProfile.FromSettings(JobHunter.Domain.Settings.CreateDefault()), cacheFolder, JobHunter.Domain.Settings.CreateDefault());

            await source.FetchAsync(context, CancellationToken.None);

            Assert.NotNull(handler.LastRequest);
            Assert.Equal("JobHunter/1.0", handler.LastRequest!.Headers.UserAgent.ToString());
        }
        finally
        {
            Directory.Delete(cacheFolder, recursive: true);
        }
    }

    [Fact]
    public async Task FetchAsync_WhenTheServerRefuses_ReturnsAnErrorInsteadOfThrowing()
    {
        FakeHttpMessageHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        using HttpClient httpClient = new(handler);
        RemoteOkSource source = new(httpClient);
        string cacheFolder = CreateTempCacheFolder();

        try
        {
            SourceFetchContext context = new(DateTimeOffset.UtcNow.AddDays(-21), CandidateProfile.FromSettings(JobHunter.Domain.Settings.CreateDefault()), cacheFolder, JobHunter.Domain.Settings.CreateDefault());

            SourceFetchResult result = await source.FetchAsync(context, CancellationToken.None);

            Assert.Empty(result.Jobs);
            Assert.Equal(0, result.FetchedCount);
            Assert.NotNull(result.Error);
            Assert.Empty(Directory.GetFiles(cacheFolder));
        }
        finally
        {
            Directory.Delete(cacheFolder, recursive: true);
        }
    }

    [LiveFact]
    public async Task FetchAsync_WithLiveNetwork_ReturnsAtLeastOneJob()
    {
        using HttpClient httpClient = new();
        RemoteOkSource source = new(httpClient);
        string cacheFolder = CreateTempCacheFolder();

        try
        {
            SourceFetchContext context = new(DateTimeOffset.UtcNow.AddDays(-21), CandidateProfile.FromSettings(JobHunter.Domain.Settings.CreateDefault()), cacheFolder, JobHunter.Domain.Settings.CreateDefault());

            SourceFetchResult result = await source.FetchAsync(context, CancellationToken.None);

            Assert.Null(result.Error);
            Assert.True(result.Jobs.Count >= 1, $"Expected at least one RemoteOK job, got {result.Jobs.Count}. Error: {result.Error}");
        }
        finally
        {
            Directory.Delete(cacheFolder, recursive: true);
        }
    }

    private static RawJob ParseOnePosting(string description)
    {
        string json = JsonSerializer.Serialize(new object[]
        {
            new { legal = "notice" },
            new { id = "1", position = "Backend Engineer", company = "Acme", url = "https://remoteok.com/remote-jobs/a", description, tags = Array.Empty<string>() }
        });

        return Assert.Single(RemoteOkParser.Parse(json));
    }

    private static RawJob FindJob(string sourceId)
    {
        return RemoteOkParser.Parse(ReadFixture()).Single(job => job.SourceId == sourceId);
    }

    private static string ReadFixture()
    {
        return File.ReadAllText(FixturePath);
    }

    private static string CreateTempCacheFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        return folder;
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;

            return Task.FromResult(respond(request));
        }
    }
}
