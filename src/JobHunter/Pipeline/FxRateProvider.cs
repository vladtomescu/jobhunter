using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using JobHunter.Data;

namespace JobHunter.Pipeline;

/// <summary>Supplies the exchange rate between any two currencies: how many units of a currency one unit of a base currency buys.</summary>
public interface IFxRateProvider
{
    /// <summary>Returns how many units of the currency one unit of the base currency buys, or null when either currency has no rate; a currency against itself is always one.</summary>
    /// <remarks>The settings type is written qualified because the JobHunter.Settings namespace shadows the plain name.</remarks>
    Task<decimal?> GetUnitsPerBaseAsync(string currencyCode, string baseCurrency, Domain.Settings settings, CancellationToken cancellationToken);
}

/// <summary>The daily reference rates of the European Central Bank, cached on disk for a day, crossed through the euro and overridable from the settings.</summary>
/// <remarks>The overrides are units per the saved base currency, so every rate is first read against the saved base and a pair is the quotient of two such rates: units per base = units of the currency ÷ units of the base.</remarks>
public sealed class EcbFxRateProvider(IHttpClientFactory httpClientFactory, DataPaths dataPaths) : IFxRateProvider
{
    /// <summary>Where the daily reference rates are published.</summary>
    public const string DailyRatesUrl = "https://www.ecb.europa.eu/stats/eurofxref/eurofxref-daily.xml";

    /// <summary>The name of the cached copy under the exchange-rate folder.</summary>
    public const string CacheFileName = "eurofxref-daily.xml";

    /// <summary>The currency the central bank quotes every other rate against.</summary>
    public const string QuoteCurrency = "EUR";

    private static readonly XNamespace RatesNamespace = "http://www.ecb.int/vocabulary/2002-08-01/eurofxref";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan RetryCooldown = TimeSpan.FromMinutes(15);
    private static readonly IReadOnlyDictionary<string, decimal> NoRates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

    private readonly SemaphoreSlim gate = new(1, 1);
    private IReadOnlyDictionary<string, decimal>? published;
    private DateTimeOffset lastDownloadAttempt = DateTimeOffset.MinValue;

    /// <inheritdoc />
    public async Task<decimal?> GetUnitsPerBaseAsync(string currencyCode, string baseCurrency, Domain.Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (string.IsNullOrWhiteSpace(currencyCode) || string.IsNullOrWhiteSpace(baseCurrency))
        {
            return null;
        }

        string code = Normalized(currencyCode);
        string baseCode = Normalized(baseCurrency);
        if (string.Equals(code, baseCode, StringComparison.Ordinal))
        {
            return 1m;
        }

        decimal? unitsOfCurrency = await GetUnitsPerSavedBaseAsync(code, settings, cancellationToken);
        decimal? unitsOfBase = await GetUnitsPerSavedBaseAsync(baseCode, settings, cancellationToken);

        return unitsOfCurrency is decimal currencyUnits && unitsOfBase is decimal baseUnits && currencyUnits > 0m && baseUnits > 0m
            ? currencyUnits / baseUnits
            : null;
    }

    /// <summary>Reads the currency codes and rates out of one daily reference file.</summary>
    public static IReadOnlyDictionary<string, decimal> ParseDailyRates(string xml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);

        Dictionary<string, decimal> rates = new(StringComparer.OrdinalIgnoreCase) { [QuoteCurrency] = 1m };

        foreach (XElement cube in XDocument.Parse(xml).Descendants(RatesNamespace + "Cube"))
        {
            string? currency = cube.Attribute("currency")?.Value;
            string? rate = cube.Attribute("rate")?.Value;

            if (!string.IsNullOrWhiteSpace(currency) && decimal.TryParse(rate, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed))
            {
                rates[currency.Trim().ToUpperInvariant()] = parsed;
            }
        }

        return rates;
    }

    /// <summary>Reads the manual overrides from the settings; anything that is not a readable currency-to-rate object counts as no overrides at all.</summary>
    public static IReadOnlyDictionary<string, decimal> ParseOverrides(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return NoRates;
        }

        try
        {
            Dictionary<string, decimal>? parsed = JsonSerializer.Deserialize<Dictionary<string, decimal>>(json);
            if (parsed is null)
            {
                return NoRates;
            }

            Dictionary<string, decimal> overrides = new(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, decimal> entry in parsed)
            {
                overrides[entry.Key.Trim().ToUpperInvariant()] = entry.Value;
            }

            return overrides;
        }
        catch (JsonException)
        {
            return NoRates;
        }
    }

    /// <summary>Returns the published rates, downloading them when the cached copy is missing or older than a day.</summary>
    public async Task<IReadOnlyDictionary<string, decimal>> GetPublishedRatesAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            string cacheFile = Path.Combine(dataPaths.Fx, CacheFileName);
            bool cacheIsFresh = File.Exists(cacheFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cacheFile) < CacheLifetime;

            if (published is not null && (cacheIsFresh || DateTimeOffset.UtcNow - lastDownloadAttempt < RetryCooldown))
            {
                return published;
            }

            if (!cacheIsFresh)
            {
                lastDownloadAttempt = DateTimeOffset.UtcNow;
                await TryDownloadAsync(cacheFile, cancellationToken);
            }

            published = File.Exists(cacheFile) ? ReadCache(cacheFile) : NoRates;

            return published;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Units of the currency per one unit of the saved base currency: the override when one names the currency, otherwise the central bank's cross rate through the euro.</summary>
    private async Task<decimal?> GetUnitsPerSavedBaseAsync(string code, Domain.Settings settings, CancellationToken cancellationToken)
    {
        string savedBase = string.IsNullOrWhiteSpace(settings.BaseCurrency) ? QuoteCurrency : Normalized(settings.BaseCurrency);
        if (string.Equals(code, savedBase, StringComparison.Ordinal))
        {
            return 1m;
        }

        if (ParseOverrides(settings.FxOverridesJson).TryGetValue(code, out decimal overridden))
        {
            return overridden;
        }

        IReadOnlyDictionary<string, decimal> rates = await GetPublishedRatesAsync(cancellationToken);

        return rates.TryGetValue(code, out decimal unitsPerEuro) && rates.TryGetValue(savedBase, out decimal savedBaseUnitsPerEuro) && savedBaseUnitsPerEuro > 0m
            ? unitsPerEuro / savedBaseUnitsPerEuro
            : null;
    }

    private static string Normalized(string currencyCode)
    {
        return currencyCode.Trim().ToUpperInvariant();
    }

    private static IReadOnlyDictionary<string, decimal> ReadCache(string cacheFile)
    {
        try
        {
            return ParseDailyRates(File.ReadAllText(cacheFile));
        }
        catch (Exception exception) when (exception is IOException or XmlException or ArgumentException)
        {
            return NoRates;
        }
    }

    private async Task TryDownloadAsync(string cacheFile, CancellationToken cancellationToken)
    {
        try
        {
            HttpClient client = httpClientFactory.CreateClient(nameof(EcbFxRateProvider));
            string xml = await client.GetStringAsync(DailyRatesUrl, cancellationToken);
            ParseDailyRates(xml);
            await File.WriteAllTextAsync(cacheFile, xml, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException or XmlException or ArgumentException)
        {
        }
    }
}
