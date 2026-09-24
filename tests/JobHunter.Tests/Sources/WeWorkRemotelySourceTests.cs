using System.Net;
using System.Text;
using JobHunter.Pipeline;
using JobHunter.Sources;
using JobHunter.Sources.WeWorkRemotely;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Tests.Sources;

/// <summary>Proves the We Work Remotely parser maps the fixture fields correctly and the source fetches, caches and never throws.</summary>
public sealed class WeWorkRemotelySourceTests
{
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "wwr-sample.rss");

    [Fact]
    public void Parse_FromFixture_ReturnsThreeItems()
    {
        IReadOnlyList<RawJob> jobs = WeWorkRemotelyParser.Parse(ReadFixture());

        Assert.Equal(3, jobs.Count);
    }

    [Fact]
    public void Parse_FromFixture_SplitsCompanyTitleAtTheFirstColonAndCapturesRegionAndType()
    {
        RawJob job = FindJob("https://weworkremotely.com/remote-jobs/reddit-backend-engineer-iam");

        Assert.Equal(JobSourceKind.WeWorkRemotely, job.Source);
        Assert.Equal("Reddit", job.Company);
        Assert.Equal("Backend Engineer, IAM", job.Title);
        Assert.Equal("Anywhere in the World", job.RegionText);
        Assert.Equal("Full-Time", job.EmploymentType);
        Assert.Null(job.LocationText);
        Assert.Null(job.Ats);
        Assert.Equal("https://weworkremotely.com/remote-jobs/reddit-backend-engineer-iam", job.PostingUrl);
        Assert.Equal(job.PostingUrl, job.ApplyUrl);
        Assert.Equal(new DateTimeOffset(2026, 9, 14, 7, 31, 7, TimeSpan.Zero), job.PostedAt);
        Assert.StartsWith("<p>", job.DescriptionRaw, StringComparison.Ordinal);
        Assert.Empty(job.Tags);
    }

    [Fact]
    public void Parse_FromFixture_CapturesCountryStateAndSkillsWhenPresent()
    {
        RawJob job = FindJob("https://weworkremotely.com/remote-jobs/collaboration-ai-senior-software-ai-engineer");

        Assert.Equal("Collaboration.Ai", job.Company);
        Assert.Equal("Senior Software AI Engineer", job.Title);
        Assert.Equal("\U0001F1FA\U0001F1F8 United States of America, Minnesota", job.LocationText);
        Assert.Equal<string>(["Node.js", "PostgreSQL", "Python", "Large Language Models (LLMs)", "Kotlin", "TypeScript", "AI Agents", "Agentic AI"], job.Tags);
        Assert.Equal(new DateTimeOffset(2026, 8, 17, 11, 57, 38, TimeSpan.Zero), job.PostedAt);
    }

    [Fact]
    public void Parse_FromFixture_LeavesLocationTextNullWhenCountryAndStateAreEmpty()
    {
        RawJob job = FindJob("https://weworkremotely.com/remote-jobs/toptal-power-platform-solutions-architect");

        Assert.Equal("Toptal", job.Company);
        Assert.Equal("Power Platform Solutions Architect", job.Title);
        Assert.Equal("North America Only", job.RegionText);
        Assert.Null(job.LocationText);
        Assert.Equal(new DateTimeOffset(2026, 9, 10, 21, 30, 33, TimeSpan.Zero), job.PostedAt);
    }

    [Fact]
    public void Parse_WithATitleThatHasNoColon_KeepsTheWholeTextAsTitle()
    {
        string rss = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0"><channel><item>
          <title>Senior Backend Engineer</title>
          <region>Anywhere in the World</region>
          <country></country>
          <state></state>
          <skills></skills>
          <type>Full-Time</type>
          <description>&lt;p&gt;Hi&lt;/p&gt;</description>
          <pubDate>Mon, 14 Sep 2026 07:31:07 +0000</pubDate>
          <guid>https://weworkremotely.com/remote-jobs/no-colon</guid>
          <link>https://weworkremotely.com/remote-jobs/no-colon</link>
        </item></channel></rss>
        """;

        RawJob job = Assert.Single(WeWorkRemotelyParser.Parse(rss));

        Assert.Equal("Unknown", job.Company);
        Assert.Equal("Senior Backend Engineer", job.Title);
    }

    [Fact]
    public void Kind_And_IsFullSnapshot_MatchTheSourceContract()
    {
        WeWorkRemotelySource source = new(new HttpClient());

        Assert.Equal(JobSourceKind.WeWorkRemotely, source.Kind);
        Assert.False(source.IsFullSnapshot);
    }

    [Fact]
    public void AddWeWorkRemotelySource_Registers_AResolvableJobSource()
    {
        ServiceCollection services = new();
        services.AddWeWorkRemotelySource();
        using ServiceProvider provider = services.BuildServiceProvider();

        IJobSource resolved = provider.GetRequiredService<IJobSource>();

        Assert.IsType<WeWorkRemotelySource>(resolved);
    }

    [Fact]
    public async Task FetchAsync_WithASuccessfulResponse_CachesEachFeedAndReturnsParsedJobs()
    {
        string fixtureText = ReadFixture();
        FakeHttpMessageHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(fixtureText, Encoding.UTF8, "application/rss+xml")
        });
        using HttpClient httpClient = new(handler);
        WeWorkRemotelySource source = new(httpClient);
        string cacheFolder = CreateTempCacheFolder();

        try
        {
            JobHunter.Domain.Settings settings = JobHunter.Domain.Settings.CreateDefault();
            settings.ConfigureContact("Test", "User", "test@example.com", string.Empty, string.Empty, string.Empty);
            SourceFetchContext context = new(DateTimeOffset.UtcNow.AddDays(-21), CandidateProfile.FromSettings(settings), cacheFolder, settings);

            SourceFetchResult result = await source.FetchAsync(context, CancellationToken.None);

            Assert.Null(result.Error);
            Assert.Equal(9, result.FetchedCount);
            Assert.Equal(9, result.Jobs.Count);
            Assert.Equal(3, handler.Requests.Count);
            Assert.Equal(3, Directory.GetFiles(cacheFolder).Length);
            foreach (HttpRequestMessage request in handler.Requests)
            {
                string userAgent = request.Headers.UserAgent.ToString();
                Assert.StartsWith("JobHunter/1.0 (+mailto:", userAgent, StringComparison.Ordinal);
                Assert.Contains("test@example.com", userAgent, StringComparison.Ordinal);
            }
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
            Content = new StringContent(ReadFixture(), Encoding.UTF8, "application/rss+xml")
        });
        using HttpClient httpClient = new(handler);
        WeWorkRemotelySource source = new(httpClient);
        string cacheRoot = Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"));
        string cacheFolder = Path.Combine(cacheRoot, "weworkremotely", "2026-09-15");

        try
        {
            SourceFetchContext context = new(DateTimeOffset.UtcNow.AddDays(-21), CandidateProfile.FromSettings(JobHunter.Domain.Settings.CreateDefault()), cacheFolder, JobHunter.Domain.Settings.CreateDefault());

            SourceFetchResult result = await source.FetchAsync(context, CancellationToken.None);

            Assert.Null(result.Error);
            Assert.Equal(9, result.Jobs.Count);
            Assert.True(Directory.Exists(cacheFolder));
            Assert.Equal(3, Directory.GetFiles(cacheFolder).Length);
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
            Content = new StringContent(ReadFixture(), Encoding.UTF8, "application/rss+xml")
        });
        using HttpClient httpClient = new(handler);
        WeWorkRemotelySource source = new(httpClient);
        string cacheFolder = CreateTempCacheFolder();

        try
        {
            SourceFetchContext context = new(DateTimeOffset.UtcNow.AddDays(-21), CandidateProfile.FromSettings(JobHunter.Domain.Settings.CreateDefault()), cacheFolder, JobHunter.Domain.Settings.CreateDefault());

            await source.FetchAsync(context, CancellationToken.None);

            Assert.All(handler.Requests, request => Assert.Equal("JobHunter/1.0", request.Headers.UserAgent.ToString()));
        }
        finally
        {
            Directory.Delete(cacheFolder, recursive: true);
        }
    }

    [Fact]
    public async Task FetchAsync_WhenOneFeedFails_KeepsTheJobsFromTheFeedsThatSucceededAndReportsTheError()
    {
        string fixtureText = ReadFixture();
        int callCount = 0;
        FakeHttpMessageHandler handler = new(_ =>
        {
            callCount++;

            return callCount == 2
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(fixtureText, Encoding.UTF8, "application/rss+xml") };
        });
        using HttpClient httpClient = new(handler);
        WeWorkRemotelySource source = new(httpClient);
        string cacheFolder = CreateTempCacheFolder();

        try
        {
            SourceFetchContext context = new(DateTimeOffset.UtcNow.AddDays(-21), CandidateProfile.FromSettings(JobHunter.Domain.Settings.CreateDefault()), cacheFolder, JobHunter.Domain.Settings.CreateDefault());

            SourceFetchResult result = await source.FetchAsync(context, CancellationToken.None);

            Assert.NotNull(result.Error);
            Assert.Equal(6, result.Jobs.Count);
            Assert.Equal(6, result.FetchedCount);
            Assert.Equal(2, Directory.GetFiles(cacheFolder).Length);
        }
        finally
        {
            Directory.Delete(cacheFolder, recursive: true);
        }
    }

    [LiveFact]
    public async Task FetchAsync_WithLiveNetwork_ReturnsAtLeastOneJobFromEachOfTheThreeFeeds()
    {
        using HttpClient httpClient = new();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(SourceUserAgent.Build(string.Empty));

        foreach (string feedUrl in WeWorkRemotelyFeeds.CategoryUrls)
        {
            string responseText = await httpClient.GetStringAsync(feedUrl);
            IReadOnlyList<RawJob> feedJobs = WeWorkRemotelyParser.Parse(responseText);

            Assert.True(feedJobs.Count >= 1, $"Expected at least one job from {feedUrl}, got {feedJobs.Count}.");
        }
    }

    [LiveFact]
    public async Task FetchAsync_WithLiveNetwork_ReturnsAtLeastOneJobThroughTheSource()
    {
        using HttpClient httpClient = new();
        WeWorkRemotelySource source = new(httpClient);
        string cacheFolder = CreateTempCacheFolder();

        try
        {
            SourceFetchContext context = new(DateTimeOffset.UtcNow.AddDays(-21), CandidateProfile.FromSettings(JobHunter.Domain.Settings.CreateDefault()), cacheFolder, JobHunter.Domain.Settings.CreateDefault());

            SourceFetchResult result = await source.FetchAsync(context, CancellationToken.None);

            Assert.Null(result.Error);
            Assert.True(result.Jobs.Count >= 1, $"Expected at least one We Work Remotely job, got {result.Jobs.Count}. Error: {result.Error}");
        }
        finally
        {
            Directory.Delete(cacheFolder, recursive: true);
        }
    }

    private static RawJob FindJob(string sourceId)
    {
        return WeWorkRemotelyParser.Parse(ReadFixture()).Single(job => job.SourceId == sourceId);
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
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            return Task.FromResult(respond(request));
        }
    }
}
