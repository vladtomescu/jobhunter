using JobHunter.Domain;
using JobHunter.Pipeline;

namespace JobHunter.Tests.Pipeline;

/// <summary>Proves that compensation from any period and any of the currencies the sources quote ends up as euro per year.</summary>
public sealed class CompNormalizerTests
{
    private readonly CompNormalizer normalizer = new(new FakeFxRateProvider());
    private readonly JobHunter.Domain.Settings settings = JobHunter.Domain.Settings.CreateDefault();

    [Theory]
    [InlineData(45, "EUR", CompPeriod.Hour, 79_200)]
    [InlineData(500, "EUR", CompPeriod.Day, 110_000)]
    [InlineData(8_000, "EUR", CompPeriod.Month, 96_000)]
    [InlineData(100_000, "EUR", CompPeriod.Year, 100_000)]
    [InlineData(100_000, "USD", CompPeriod.Year, 80_000)]
    [InlineData(80_000, "GBP", CompPeriod.Year, 100_000)]
    [InlineData(500_000, "SEK", CompPeriod.Year, 50_000)]
    [InlineData(60, "USD", CompPeriod.Hour, 84_480)]
    [InlineData(20_000, "SEK", CompPeriod.Month, 24_000)]
    public async Task ToEurPerYearAsync_ForStatedComp_ReturnsEuroPerYear(int amount, string currency, CompPeriod period, int expected)
    {
        EurYearComp comp = await normalizer.ToEurPerYearAsync(amount, amount, currency, period, settings, CancellationToken.None);

        Assert.Equal((decimal)expected, comp.MinEurYear);
        Assert.Equal((decimal)expected, comp.MaxEurYear);
    }

    [Fact]
    public async Task ToEurPerYearAsync_ForARange_ConvertsBothBounds()
    {
        EurYearComp comp = await normalizer.ToEurPerYearAsync(100_000m, 150_000m, "USD", CompPeriod.Year, settings, CancellationToken.None);

        Assert.Equal(80_000m, comp.MinEurYear);
        Assert.Equal(120_000m, comp.MaxEurYear);
        Assert.Equal(120_000m, comp.Headline);
    }

    [Fact]
    public async Task ToEurPerYearAsync_WithOnlyAnUpperBound_LeavesTheLowerBoundUnknown()
    {
        EurYearComp comp = await normalizer.ToEurPerYearAsync(null, 120_000m, "EUR", CompPeriod.Year, settings, CancellationToken.None);

        Assert.Null(comp.MinEurYear);
        Assert.Equal(120_000m, comp.MaxEurYear);
        Assert.False(comp.IsUnknown);
    }

    [Fact]
    public async Task ToEurPerYearAsync_WithoutAnyFigure_ReturnsUnknown()
    {
        EurYearComp comp = await normalizer.ToEurPerYearAsync(null, null, "EUR", CompPeriod.Year, settings, CancellationToken.None);

        Assert.True(comp.IsUnknown);
        Assert.Null(comp.Headline);
    }

    [Fact]
    public async Task ToEurPerYearAsync_ForACurrencyWithoutARate_ReturnsUnknown()
    {
        EurYearComp comp = await normalizer.ToEurPerYearAsync(100_000m, 120_000m, "XYZ", CompPeriod.Year, settings, CancellationToken.None);

        Assert.True(comp.IsUnknown);
    }

    [Fact]
    public async Task ToEurPerYearAsync_WithoutACurrency_ReadsTheFigureAsEuro()
    {
        EurYearComp comp = await normalizer.ToEurPerYearAsync(90_000m, null, null, CompPeriod.Year, settings, CancellationToken.None);

        Assert.Equal(90_000m, comp.MinEurYear);
    }

    [Fact]
    public async Task ToEurPerYearAsync_WithoutAPeriod_ReadsTheFigureAsYearly()
    {
        EurYearComp comp = await normalizer.ToEurPerYearAsync(90_000m, null, "EUR", null, settings, CancellationToken.None);

        Assert.Equal(90_000m, comp.MinEurYear);
    }

    [Theory]
    [InlineData(CompPeriod.Hour, 1760)]
    [InlineData(CompPeriod.Day, 220)]
    [InlineData(CompPeriod.Month, 12)]
    [InlineData(CompPeriod.Year, 1)]
    [InlineData(null, 1)]
    public void PeriodsPerYear_ForAPeriod_ReturnsTheAnnualFactor(CompPeriod? period, int expected)
    {
        Assert.Equal((decimal)expected, CompNormalizer.PeriodsPerYear(period));
    }

    [Theory]
    [InlineData("HOUR", CompPeriod.Hour)]
    [InlineData("hourly", CompPeriod.Hour)]
    [InlineData("DAY", CompPeriod.Day)]
    [InlineData("MONTH", CompPeriod.Month)]
    [InlineData("YEAR", CompPeriod.Year)]
    [InlineData("annual", CompPeriod.Year)]
    [InlineData("fortnight", null)]
    [InlineData(null, null)]
    public void ParsePeriod_ForWhatASourceReports_ReturnsThePeriod(string? reported, CompPeriod? expected)
    {
        Assert.Equal(expected, CompNormalizer.ParsePeriod(reported));
    }
}
