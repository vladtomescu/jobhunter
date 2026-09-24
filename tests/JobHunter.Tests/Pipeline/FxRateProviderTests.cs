using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Pipeline;

namespace JobHunter.Tests.Pipeline;

/// <summary>Proves that the daily reference rates are read correctly, served from the cache without reaching the network, crossed through the euro for any base currency, and beaten by a manual override.</summary>
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
    public async Task GetUnitsPerBaseAsync_WithAFreshCachedFileAndAEuroBase_AnswersThePublishedRateWithoutReachingTheNetwork()
    {
        EcbFxRateProvider provider = NewProviderWithCachedRates();

        Assert.Equal(1.1551m, await provider.GetUnitsPerBaseAsync("USD", "EUR", NewSettings(null), CancellationToken.None));
        Assert.Equal(10.7670m, await provider.GetUnitsPerBaseAsync("nok", "EUR", NewSettings(null), CancellationToken.None));
    }

    [Fact]
    public async Task GetUnitsPerBaseAsync_ForACurrencyThatIsNotPublished_ReturnsNothing()
    {
        EcbFxRateProvider provider = NewProviderWithCachedRates();

        Assert.Null(await provider.GetUnitsPerBaseAsync("XYZ", "EUR", NewSettings(null), CancellationToken.None));
        Assert.Null(await provider.GetUnitsPerBaseAsync("USD", "XYZ", NewSettings(null), CancellationToken.None));
    }

    [Theory]
    [InlineData("EUR", "EUR")]
    [InlineData("USD", "USD")]
    [InlineData("XYZ", "xyz")]
    public async Task GetUnitsPerBaseAsync_ForACurrencyAgainstItself_ReturnsOne(string currency, string baseCurrency)
    {
        EcbFxRateProvider provider = NewProviderWithCachedRates();

        Assert.Equal(1m, await provider.GetUnitsPerBaseAsync(currency, baseCurrency, NewSettings(null), CancellationToken.None));
    }

    [Theory]
    [InlineData("EUR", "USD", 0.865726)]
    [InlineData("GBP", "USD", 0.741044)]
    [InlineData("NOK", "USD", 9.321271)]
    [InlineData("SEK", "GBP", 13.179046)]
    public async Task GetUnitsPerBaseAsync_ForAPairOtherThanTheEuro_CrossesThePublishedRatesThroughTheEuro(string currency, string baseCurrency, double expected)
    {
        EcbFxRateProvider provider = NewProviderWithCachedRates();

        decimal? rate = await provider.GetUnitsPerBaseAsync(currency, baseCurrency, NewSettings(null), CancellationToken.None);

        Assert.Equal((decimal)expected, decimal.Round(rate!.Value, 6));
    }

    [Fact]
    public async Task GetUnitsPerBaseAsync_WithADollarBaseSavedAndNoOverride_CrossesThroughTheEuro()
    {
        EcbFxRateProvider provider = NewProviderWithCachedRates();

        decimal? rate = await provider.GetUnitsPerBaseAsync("GBP", "USD", NewSettings(null, baseCurrency: "USD"), CancellationToken.None);

        Assert.Equal(0.741044m, decimal.Round(rate!.Value, 6));
    }

    [Fact]
    public async Task GetUnitsPerBaseAsync_WhenTheSettingsOverrideACurrency_UsesTheOverride()
    {
        EcbFxRateProvider provider = NewProviderWithCachedRates();

        decimal? rate = await provider.GetUnitsPerBaseAsync("USD", "EUR", NewSettings("{\"USD\": 1.05, \"NOK\": 10.0}"), CancellationToken.None);

        Assert.Equal(1.05m, rate);
    }

    [Fact]
    public async Task GetUnitsPerBaseAsync_WhenTheOverrideNamesAnUnpublishedCurrency_UsesTheOverride()
    {
        EcbFxRateProvider provider = NewProviderWithCachedRates();

        Assert.Equal(2.5m, await provider.GetUnitsPerBaseAsync("XYZ", "EUR", NewSettings("{\"xyz\": 2.5}"), CancellationToken.None));
    }

    [Fact]
    public async Task GetUnitsPerBaseAsync_WithADollarBaseSaved_ReadsTheOverridesAsUnitsPerDollar()
    {
        EcbFxRateProvider provider = NewProviderWithCachedRates();
        JobHunter.Domain.Settings settings = NewSettings("{\"EUR\": 0.9}", baseCurrency: "USD");

        Assert.Equal(0.9m, await provider.GetUnitsPerBaseAsync("EUR", "USD", settings, CancellationToken.None));
        Assert.Equal(1m / 0.9m, await provider.GetUnitsPerBaseAsync("USD", "EUR", settings, CancellationToken.None));
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

    private static JobHunter.Domain.Settings NewSettings(string? overridesJson, string baseCurrency = "EUR")
    {
        JobHunter.Domain.Settings settings = JobHunter.Domain.Settings.CreateDefault();
        settings.ConfigureFxOverrides(overridesJson);
        settings.ConfigureCandidate(null, true, true, "en", baseCurrency, string.Empty, ContractPreference.Either, false, null, settings.TitleIncludeTerms, settings.TitleExcludeTerms);

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
