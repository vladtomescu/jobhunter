using JobHunter.Domain;
using JobHunter.Pipeline;

namespace JobHunter.Tests.Pipeline;

/// <summary>Proves that compensation from any period and any of the currencies the sources quote ends up as the base currency per year.</summary>
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
    public async Task ToBasePerYearAsync_ForStatedCompAndAEuroBase_ReturnsEuroPerYear(int amount, string currency, CompPeriod period, int expected)
    {
        YearlyComp comp = await normalizer.ToBasePerYearAsync(amount, amount, currency, period, "EUR", settings, CancellationToken.None);

        Assert.Equal((decimal)expected, comp.MinPerYear);
        Assert.Equal((decimal)expected, comp.MaxPerYear);
    }

    [Theory]
    [InlineData(100_000, "EUR", CompPeriod.Year, 125_000)]
    [InlineData(80_000, "GBP", CompPeriod.Year, 125_000)]
    [InlineData(500_000, "SEK", CompPeriod.Year, 62_500)]
    [InlineData(45, "USD", CompPeriod.Hour, 79_200)]
    [InlineData(8_000, "EUR", CompPeriod.Month, 120_000)]
    public async Task ToBasePerYearAsync_ForStatedCompAndADollarBase_ReturnsDollarsPerYear(int amount, string currency, CompPeriod period, int expected)
    {
        YearlyComp comp = await normalizer.ToBasePerYearAsync(amount, amount, currency, period, "USD", settings, CancellationToken.None);

        Assert.Equal((decimal)expected, comp.MinPerYear);
        Assert.Equal((decimal)expected, comp.MaxPerYear);
    }

    [Fact]
    public async Task ToBasePerYearAsync_WithoutACurrencyAndADollarBase_ReadsTheFigureAsDollars()
    {
        YearlyComp comp = await normalizer.ToBasePerYearAsync(90_000m, null, null, CompPeriod.Year, "USD", settings, CancellationToken.None);

        Assert.Equal(90_000m, comp.MinPerYear);
    }

    [Fact]
    public async Task ToBasePerYearAsync_ForARange_ConvertsBothBounds()
    {
        YearlyComp comp = await normalizer.ToBasePerYearAsync(100_000m, 150_000m, "USD", CompPeriod.Year, "EUR", settings, CancellationToken.None);

        Assert.Equal(80_000m, comp.MinPerYear);
        Assert.Equal(120_000m, comp.MaxPerYear);
        Assert.Equal(120_000m, comp.Headline);
    }

    [Fact]
    public async Task ToBasePerYearAsync_WithOnlyAnUpperBound_LeavesTheLowerBoundUnknown()
    {
        YearlyComp comp = await normalizer.ToBasePerYearAsync(null, 120_000m, "EUR", CompPeriod.Year, "EUR", settings, CancellationToken.None);

        Assert.Null(comp.MinPerYear);
        Assert.Equal(120_000m, comp.MaxPerYear);
        Assert.False(comp.IsUnknown);
    }

    [Fact]
    public async Task ToBasePerYearAsync_WithoutAnyFigure_ReturnsUnknown()
    {
        YearlyComp comp = await normalizer.ToBasePerYearAsync(null, null, "EUR", CompPeriod.Year, "EUR", settings, CancellationToken.None);

        Assert.True(comp.IsUnknown);
        Assert.Null(comp.Headline);
    }

    [Fact]
    public async Task ToBasePerYearAsync_ForACurrencyWithoutARate_ReturnsUnknown()
    {
        YearlyComp comp = await normalizer.ToBasePerYearAsync(100_000m, 120_000m, "XYZ", CompPeriod.Year, "EUR", settings, CancellationToken.None);

        Assert.True(comp.IsUnknown);
    }

    [Fact]
    public async Task ToBasePerYearAsync_WithoutACurrency_ReadsTheFigureAsTheBaseCurrency()
    {
        YearlyComp comp = await normalizer.ToBasePerYearAsync(90_000m, null, null, CompPeriod.Year, "EUR", settings, CancellationToken.None);

        Assert.Equal(90_000m, comp.MinPerYear);
    }

    [Fact]
    public async Task ToBasePerYearAsync_WithoutAPeriod_ReadsTheFigureAsYearly()
    {
        YearlyComp comp = await normalizer.ToBasePerYearAsync(90_000m, null, "EUR", null, "EUR", settings, CancellationToken.None);

        Assert.Equal(90_000m, comp.MinPerYear);
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
