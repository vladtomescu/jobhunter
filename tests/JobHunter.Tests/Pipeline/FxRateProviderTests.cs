using JobHunter.Data;
using JobHunter.Pipeline;

namespace JobHunter.Tests.Pipeline;

/// <summary>Proves that the daily reference rates are read correctly, served from the cache without reaching the network, and beaten by a manual override.</summary>
public sealed class FxRateProviderTests : IDisposable
{
    private const string FixtureName = "ecb-eurofxref-daily.xml";

    private readonly string dataFolder = Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ParseDailyRates_ForAPublishedFile_ReadsEveryCurrency()
    {
        IReadOnlyDictionary<string, decimal> rates = EcbFxRateProvider.ParseDailyRates(ReadFixture());

        Assert.Equal(1m, rates["EUR"]);
        Assert.Equal(1.1551m, rates["USD"]);
        Assert.Equal(0.85598m, rates["GBP"]);
        Assert.Equal(10.7670m, rates["NOK"]);
        Assert.Equal(11.2810m, rates["SEK"]);
    }

    [Fact]
    public void ParseDailyRates_ForAFileWithoutRates_ReturnsOnlyTheBaseCurrency()
    {
        const string Xml = "<gesmes:Envelope xmlns:gesmes=\"http://www.gesmes.org/xml/2002-08-01\" xmlns=\"http://www.ecb.int/vocabulary/2002-08-01/eurofxref\"><Cube /></gesmes:Envelope>";

        Assert.Equal<string>(["EUR"], EcbFxRateProvider.ParseDailyRates(Xml).Keys);
    }

    [Fact]
    public async Task GetUnitsPerEuroAsync_WithAFreshCachedFile_AnswersWithoutReachingTheNetwork()
    {
        EcbFxRateProvider provider = NewProviderWithCachedRates();

        Assert.Equal(1.1551m, await provider.GetUnitsPerEuroAsync("USD", NewSettings(null), CancellationToken.None));
        Assert.Equal(10.7670m, await provider.GetUnitsPerEuroAsync("nok", NewSettings(null), CancellationToken.None));
    }

    [Fact]
    public async Task GetUnitsPerEuroAsync_ForACurrencyThatIsNotPublished_ReturnsNothing()
    {
        EcbFxRateProvider provider = NewProviderWithCachedRates();

        Assert.Null(await provider.GetUnitsPerEuroAsync("XYZ", NewSettings(null), CancellationToken.None));
    }

    [Fact]
    public async Task GetUnitsPerEuroAsync_ForTheEuro_ReturnsOne()
    {
        EcbFxRateProvider provider = NewProviderWithCachedRates();

        Assert.Equal(1m, await provider.GetUnitsPerEuroAsync("EUR", NewSettings(null), CancellationToken.None));
    }

    [Fact]
    public async Task GetUnitsPerEuroAsync_WhenTheSettingsOverrideACurrency_UsesTheOverride()
    {
        EcbFxRateProvider provider = NewProviderWithCachedRates();

        decimal? rate = await provider.GetUnitsPerEuroAsync("USD", NewSettings("{\"USD\": 1.05, \"NOK\": 10.0}"), CancellationToken.None);

        Assert.Equal(1.05m, rate);
    }

    [Fact]
    public async Task GetUnitsPerEuroAsync_WhenTheOverrideNamesAnUnpublishedCurrency_UsesTheOverride()
    {
        EcbFxRateProvider provider = NewProviderWithCachedRates();

        Assert.Equal(2.5m, await provider.GetUnitsPerEuroAsync("XYZ", NewSettings("{\"xyz\": 2.5}"), CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1, 2, 3]")]
    public void ParseOverrides_ForSomethingThatIsNotARateTable_ReturnsNoOverrides(string? json)
    {
        Assert.Empty(EcbFxRateProvider.ParseOverrides(json));
    }

    [Fact]
    public void ParseOverrides_ForARateTable_ReadsItCaseInsensitively()
    {
        IReadOnlyDictionary<string, decimal> overrides = EcbFxRateProvider.ParseOverrides("{\"usd\": 1.05}");

        Assert.Equal(1.05m, overrides["USD"]);
    }

    public void Dispose()
    {
        if (Directory.Exists(dataFolder))
        {
            Directory.Delete(dataFolder, recursive: true);
        }
    }

    private static string ReadFixture()
    {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", FixtureName));
    }

    private static JobHunter.Domain.Settings NewSettings(string? overridesJson)
    {
        JobHunter.Domain.Settings settings = JobHunter.Domain.Settings.CreateDefault();
        settings.ConfigureFxOverrides(overridesJson);

        return settings;
    }

    private EcbFxRateProvider NewProviderWithCachedRates()
    {
        DataPaths paths = new(dataFolder);
        string cacheFile = Path.Combine(paths.Fx, EcbFxRateProvider.CacheFileName);
        File.WriteAllText(cacheFile, ReadFixture());

        return new EcbFxRateProvider(new UnreachableHttpClientFactory(), paths);
    }

    private sealed class UnreachableHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            throw new InvalidOperationException("The cached rates should have answered without a download.");
        }
    }
}
