using System.Net;
using System.Text;
using JobHunter.Domain;
using JobHunter.Pipeline;
using JobHunter.Sources;
using JobHunter.Sources.Karriere;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunter.Tests.Sources;

/// <summary>Proves the karriere.at searches are read from the settings, the parser maps result pages and job pages, and the source pages through every search, reads each job once and never throws.</summary>
public sealed class KarriereSourceTests
{
    private static readonly string SearchFixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "karriere-search.json");

    private static readonly string JobFixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "karriere-job.html");

    private static readonly KarrierePacing NoPause = new(TimeSpan.Zero);

    private const string YearlySalaryPage = """<script type="application/ld+json">{"@type":"JobPosting","title":"Platform Engineer","baseSalary":{"currency":"EUR","value":{"unitText":"YEAR","value":60000}}}</script>""";

    [Fact]
    public void ReadAll_ReadsKeywordsAndOptionalPlaceAndSkipsLinesWithoutKeywords()
    {
        IReadOnlyList<KarriereSearch> searches = KarriereSearch.ReadAll("Backend Developer | Graz\n\nData Engineer\r\n| Wien\nBackend Developer|Graz");

        Assert.Equal<KarriereSearch>([new KarriereSearch("Backend Developer", "Graz"), new KarriereSearch("Data Engineer", null)], searches);
    }

    [Fact]
    public void PageUrl_EscapesTheKeywordsAndPlaceAndLeavesOutAMissingPlace()
    {
        Assert.Equal("https://www.karriere.at/jobs?keywords=C%23%20Developer&locations=Sankt%20P%C3%B6lten&page=2", new KarriereSearch("C# Developer", "Sankt Pölten").PageUrl(2).AbsoluteUri);
        Assert.Equal("https://www.karriere.at/jobs?keywords=.NET&page=1", new KarriereSearch(".NET", null).PageUrl(1).AbsoluteUri);
    }

    [Fact]
    public void ParseSearchPage_FromFixture_ReturnsTheJobsSkipsTheAdAndReadsTheNextPageFlag()
    {
        KarriereSearchPage page = KarriereParser.ParseSearchPage(File.ReadAllText(SearchFixturePath));

        Assert.True(page.HasMore);
        Assert.Equal<string>(["1000001", "1000002"], page.Listings.Select(listing => listing.Id));
        KarriereListing second = page.Listings[1];
        Assert.Equal("https://www.karriere.at/jobs/1000002", second.Link);
        Assert.Equal("Fabrikam AG", second.Company);
        Assert.Equal<string>(["Graz", "Wien"], second.Locations);
        Assert.Equal("ab 60.000 € jährlich", second.Salary);
    }

    [Fact]
    public void ParseJob_FromFixture_TakesTheJobPostingBlockAndCountsAMonthlySalaryFourteenTimes()
    {
        KarriereListing listing = KarriereParser.ParseSearchPage(File.ReadAllText(SearchFixturePath)).Listings[0];

        RawJob job = KarriereParser.ParseJob(listing, File.ReadAllText(JobFixturePath));

        Assert.Equal(JobSourceKind.Karriere, job.Source);
        Assert.Equal("1000001", job.SourceId);
        Assert.Equal("Senior Backend Engineer (m/w/d)", job.Title);
        Assert.Equal("Contoso GmbH", job.Company);
        Assert.Equal("https://contoso.example", job.CompanyUrl);
        Assert.Equal("https://www.karriere.at/jobs/1000001", job.PostingUrl);
        Assert.Equal(job.PostingUrl, job.ApplyUrl);
        Assert.StartsWith("<p>You build <strong>backend services</strong>", job.DescriptionRaw, StringComparison.Ordinal);
        Assert.Equal("Wien 2. Bezirk (Leopoldstadt); Linz", job.LocationText);
        Assert.Equal("AT", job.CountryIso);
        Assert.Null(job.IsRemote);
        Assert.Equal(56000m, job.CompMin);
        Assert.Equal(70000m, job.CompMax);
        Assert.Equal("EUR", job.CompCurrency);
        Assert.Equal(CompPeriod.Year, job.CompPeriod);
        Assert.Equal("4.000 € – 5.000 € monatlich", job.CompSummary);
        Assert.Equal("FULL_TIME", job.EmploymentType);
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.FromHours(1)), job.PostedAt);
    }

    [Fact]
    public void ParseJob_WithOnlyALegalFloor_RecordsNoPayAndKeepsTheSalaryText()
    {
        KarriereListing listing = KarriereParser.ParseSearchPage(File.ReadAllText(SearchFixturePath)).Listings[1];

        RawJob job = KarriereParser.ParseJob(listing, YearlySalaryPage);

        Assert.Null(job.CompMin);
        Assert.Null(job.CompMax);
        Assert.Null(job.CompCurrency);
        Assert.Null(job.CompPeriod);
        Assert.Equal("ab 60.000 € jährlich", job.CompSummary);
    }

    [Fact]
    public void ParseJob_WithAYearlySalaryThatIsNotAFloor_TakesItAsTheMinimum()
    {
        KarriereListing listing = KarriereParser.ParseSearchPage(File.ReadAllText(SearchFixturePath)).Listings[1] with { Salary = "60.000 € jährlich" };

        RawJob job = KarriereParser.ParseJob(listing, YearlySalaryPage);

        Assert.Equal(60000m, job.CompMin);
        Assert.Null(job.CompMax);
        Assert.Equal(CompPeriod.Year, job.CompPeriod);
    }

    [Fact]
    public void ParseJob_WithoutAJobPostingBlock_KeepsTheListingFields()
    {
        KarriereListing listing = KarriereParser.ParseSearchPage(File.ReadAllText(SearchFixturePath)).Listings[1];

        RawJob job = KarriereParser.ParseJob(listing, "<html><body>No structured data here.</body></html>");

        Assert.Equal("Platform Engineer", job.Title);
        Assert.Equal("Fabrikam AG", job.Company);
        Assert.Equal("Kubernetes platform work.", job.DescriptionRaw);
        Assert.Equal("Graz; Wien", job.LocationText);
        Assert.Null(job.CountryIso);
        Assert.Null(job.CompMin);
        Assert.Equal("ab 60.000 € jährlich", job.CompSummary);
        Assert.Equal("Vollzeit, Teilzeit", job.EmploymentType);
        Assert.Null(job.PostedAt);
    }

    [Fact]
    public void Kind_And_IsFullSnapshot_MatchTheSourceContract()
    {
        KarriereSource source = new(new HttpClient(), NoPause);

        Assert.Equal(JobSourceKind.Karriere, source.Kind);
        Assert.False(source.IsFullSnapshot);
    }

    [Fact]
    public void AddKarriereSource_Registers_AResolvableJobSource()
    {
        ServiceCollection services = new();
        services.AddKarriereSource();
        using ServiceProvider provider = services.BuildServiceProvider();

        IJobSource resolved = provider.GetRequiredService<IJobSource>();

        Assert.IsType<KarriereSource>(resolved);
    }

    [Fact]
    public async Task FetchAsync_PagesUntilTheSiteAnswersGoneAndReadsEveryListedJobOnce()
    {
        FakeHttpMessageHandler handler = new(Respond);
        using HttpClient httpClient = new(handler);
        KarriereSource source = new(httpClient, NoPause);
        string cacheFolder = CreateTempCacheFolder();

        try
        {
            SourceFetchResult result = await source.FetchAsync(Context(cacheFolder, "Backend Engineer | Wien\nPlatform Engineer"), CancellationToken.None);

            Assert.Null(result.Error);
            Assert.Equal(2, result.FetchedCount);
            Assert.Equal<string>(["1000001", "1000002"], result.Jobs.Select(job => job.SourceId));
            Assert.Equal(6, handler.Requests.Count);
            Assert.Equal(2, Directory.GetFiles(cacheFolder).Length);
            HttpRequestMessage searchRequest = handler.Requests[0];
            Assert.Equal("https://www.karriere.at/jobs?keywords=Backend%20Engineer&locations=Wien&page=1", searchRequest.RequestUri!.AbsoluteUri);
            Assert.Equal("XMLHttpRequest", Assert.Single(searchRequest.Headers.GetValues("X-Requested-With")));
            Assert.Contains("test@example.com", searchRequest.Headers.UserAgent.ToString(), StringComparison.Ordinal);
            Assert.All(handler.Requests.Where(request => request.RequestUri!.AbsolutePath.StartsWith("/jobs/", StringComparison.Ordinal)), request => Assert.False(request.Headers.Contains("X-Requested-With")));
        }
        finally
        {
            Directory.Delete(cacheFolder, recursive: true);
        }
    }

    [Fact]
    public async Task FetchAsync_WithNoSearches_SendsNoRequest()
    {
        FakeHttpMessageHandler handler = new(Respond);
        using HttpClient httpClient = new(handler);
        KarriereSource source = new(httpClient, NoPause);
        string cacheFolder = CreateTempCacheFolder();

        try
        {
            SourceFetchResult result = await source.FetchAsync(Context(cacheFolder, string.Empty), CancellationToken.None);

            Assert.Null(result.Error);
            Assert.Empty(result.Jobs);
            Assert.Empty(handler.Requests);
        }
        finally
        {
            Directory.Delete(cacheFolder, recursive: true);
        }
    }

    [Fact]
    public async Task FetchAsync_WhenAJobPageFails_KeepsTheOtherJobsAndReportsTheCount()
    {
        FakeHttpMessageHandler handler = new(request => request.RequestUri!.AbsolutePath == "/jobs/1000002"
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : Respond(request));
        using HttpClient httpClient = new(handler);
        KarriereSource source = new(httpClient, NoPause);
        string cacheFolder = CreateTempCacheFolder();

        try
        {
            SourceFetchResult result = await source.FetchAsync(Context(cacheFolder, "Backend Engineer | Wien"), CancellationToken.None);

            Assert.Equal("1000001", Assert.Single(result.Jobs).SourceId);
            Assert.Equal(2, result.FetchedCount);
            Assert.NotNull(result.Error);
            Assert.StartsWith("1 job page(s) could not be read", result.Error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(cacheFolder, recursive: true);
        }
    }

    [Fact]
    public async Task FetchAsync_WhenASearchFails_ReportsItAndStillRunsTheNextSearch()
    {
        FakeHttpMessageHandler handler = new(request => request.RequestUri!.Query.Contains("keywords=Broken", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : Respond(request));
        using HttpClient httpClient = new(handler);
        KarriereSource source = new(httpClient, NoPause);
        string cacheFolder = CreateTempCacheFolder();

        try
        {
            SourceFetchResult result = await source.FetchAsync(Context(cacheFolder, "Broken\nBackend Engineer | Wien"), CancellationToken.None);

            Assert.Equal(2, result.Jobs.Count);
            Assert.NotNull(result.Error);
            Assert.StartsWith("Broken: ", result.Error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(cacheFolder, recursive: true);
        }
    }

    [LiveFact]
    public async Task FetchAsync_WithLiveNetwork_ParsesAtLeastOneListingFromASearch()
    {
        using HttpClient httpClient = new();
        using HttpRequestMessage request = new(HttpMethod.Get, new KarriereSearch("Software Engineer", "Wien").PageUrl(1));
        request.Headers.UserAgent.ParseAdd(SourceUserAgent.Build(string.Empty));
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");

        using HttpResponseMessage response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        KarriereSearchPage page = KarriereParser.ParseSearchPage(await response.Content.ReadAsStringAsync());

        Assert.NotEmpty(page.Listings);
    }

    /// <summary>The first result page of any search is the fixture, the second is past the end, and every job page is the job fixture.</summary>
    private static HttpResponseMessage Respond(HttpRequestMessage request)
    {
        Uri url = request.RequestUri!;
        if (url.AbsolutePath == "/jobs")
        {
            return url.Query.EndsWith("page=1", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(File.ReadAllText(SearchFixturePath), Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.Gone);
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(File.ReadAllText(JobFixturePath), Encoding.UTF8, "text/html") };
    }

    private static SourceFetchContext Context(string cacheFolder, string searches)
    {
        JobHunter.Domain.Settings settings = JobHunter.Domain.Settings.CreateDefault();
        settings.ConfigureContact("Test", "User", "test@example.com", string.Empty, string.Empty, string.Empty);
        settings.ConfigureSources(remoteOkEnabled: false, wwrEnabled: false, datasetEnabled: false, datasetAtsList: string.Empty, karriereEnabled: true, karriereSearches: searches);

        return new SourceFetchContext(DateTimeOffset.UtcNow.AddDays(-21), CandidateProfile.FromSettings(settings), cacheFolder, settings);
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
