using JobHunter.Pipeline;

namespace JobHunter.Tests.Pipeline;

/// <summary>Exchange rates with round numbers, so that a converted figure can be checked by hand.</summary>
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
    public Task<decimal?> GetUnitsPerEuroAsync(string currencyCode, JobHunter.Domain.Settings settings, CancellationToken cancellationToken)
    {
        return Task.FromResult(unitsPerEuro.TryGetValue(currencyCode, out decimal rate) ? rate : (decimal?)null);
    }
}
