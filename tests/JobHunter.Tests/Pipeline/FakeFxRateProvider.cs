using JobHunter.Pipeline;

namespace JobHunter.Tests.Pipeline;

/// <summary>Exchange rates with round numbers, so that a converted figure can be checked by hand; a pair is crossed through the euro the way the central bank rates are.</summary>
public sealed class FakeFxRateProvider : IFxRateProvider
{
    private readonly Dictionary<string, decimal> unitsPerEuro = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EUR"] = 1m,
        ["USD"] = 1.25m,
        ["GBP"] = 0.80m,
        ["SEK"] = 10.00m
    };

    /// <inheritdoc />
    public Task<decimal?> GetUnitsPerBaseAsync(string currencyCode, string baseCurrency, JobHunter.Domain.Settings settings, CancellationToken cancellationToken)
    {
        bool known = unitsPerEuro.TryGetValue(currencyCode, out decimal currencyRate) & unitsPerEuro.TryGetValue(baseCurrency, out decimal baseRate);

        return Task.FromResult(known ? currencyRate / baseRate : (decimal?)null);
    }
}
