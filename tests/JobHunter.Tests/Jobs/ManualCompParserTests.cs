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
}
