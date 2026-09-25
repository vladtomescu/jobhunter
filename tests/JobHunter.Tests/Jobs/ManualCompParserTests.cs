using JobHunter.Domain;
using JobHunter.Jobs;

namespace JobHunter.Tests.Jobs;

/// <summary>Proves that the compensation typed on the add form is read the way a posting writes it.</summary>
public sealed class ManualCompParserTests
{
    [Theory]
    [InlineData("90,000 - 120,000 EUR per year", 90000, 120000, "EUR", CompPeriod.Year)]
    [InlineData("90.000 - 120.000 EUR/year", 90000, 120000, "EUR", CompPeriod.Year)]
    [InlineData("90k EUR yearly", 90000, null, "EUR", CompPeriod.Year)]
    [InlineData("$120,000", 120000, null, "USD", null)]
    [InlineData("75 GBP per hour", 75, null, "GBP", CompPeriod.Hour)]
    [InlineData("12,000 SEK monthly", 12000, null, "SEK", CompPeriod.Month)]
    [InlineData("120,000 - 90,000", 90000, 120000, null, null)]
    [InlineData("60 000 EUR per year", 60000, null, "EUR", CompPeriod.Year)]
    [InlineData("60 000 - 75 000 EUR yearly", 60000, 75000, "EUR", CompPeriod.Year)]
    [InlineData("SGD 150,000 - 180,000 per year", 150000, 180000, "SGD", CompPeriod.Year)]
    [InlineData("9,000 BRL monthly", 9000, null, "BRL", CompPeriod.Month)]
    [InlineData("90k eur yearly", 90000, null, "EUR", CompPeriod.Year)]
    [InlineData("£75 per hour", 75, null, "GBP", CompPeriod.Hour)]
    [InlineData("Remote CET, 100,000 CHF per year", 100000, null, "CHF", CompPeriod.Year)]
    public void Parse_ForStatedCompensation_ReadsTheFiguresCurrencyAndPeriod(string text, int expectedMin, int? expectedMax, string? expectedCurrency, CompPeriod? expectedPeriod)
    {
        ManualComp comp = ManualCompParser.Parse(text);
        decimal? expectedUpper = expectedMax is int max ? max : null;

        Assert.Equal((decimal?)expectedMin, comp.Min);
        Assert.Equal(expectedUpper, comp.Max);
        Assert.Equal(expectedCurrency, comp.Currency);
        Assert.Equal(expectedPeriod, comp.Period);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("competitive")]
    public void Parse_ForTextWithoutAFigure_ReportsNoCompensation(string? text)
    {
        Assert.True(ManualCompParser.Parse(text).IsUnknown);
    }

    [Theory]
    [InlineData("neural search platform, 90000 per year")]
    [InlineData("Leipzig office, 90000 per year")]
    [InlineData("caudal team, 90000 per year")]
    public void Parse_ForACurrencyCodeInsideAWord_ReadsNoCurrency(string text)
    {
        Assert.Null(ManualCompParser.Parse(text).Currency);
    }

    [Theory]
    [InlineData("90000 per year, CET hours")]
    [InlineData("the top of the band is 90000")]
    [InlineData("API team, 90000 yearly")]
    public void Parse_ForThreeLettersThatAreNoCurrencyCode_ReadsNoCurrency(string text)
    {
        Assert.Null(ManualCompParser.Parse(text).Currency);
    }

    [Theory]
    [InlineData(90000d, 120000d, "EUR", CompPeriod.Year)]
    [InlineData(62.5, null, "USD", CompPeriod.Hour)]
    [InlineData(4500d, 5200d, "CHF", CompPeriod.Month)]
    [InlineData(700, null, null, CompPeriod.Day)]
    [InlineData(95000, null, "GBP", null)]
    public void Describe_ForStoredFigures_WritesTextThatParsesBackToTheSameFigures(double min, double? max, string? currency, CompPeriod? period)
    {
        decimal? expectedMax = max is double upper ? (decimal)upper : null;

        ManualComp parsed = ManualCompParser.Parse(ManualCompParser.Describe((decimal)min, expectedMax, currency, period));

        Assert.Equal((decimal)min, parsed.Min);
        Assert.Equal(expectedMax, parsed.Max);
        Assert.Equal(currency, parsed.Currency);
        Assert.Equal(period, parsed.Period);
    }

    [Fact]
    public void Describe_WithoutAFigure_WritesNothing()
    {
        Assert.Equal(string.Empty, ManualCompParser.Describe(null, null, "EUR", CompPeriod.Year));
    }
}
