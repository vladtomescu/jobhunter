using JobHunter.Pipeline;

namespace JobHunter.Tests.Pipeline;

/// <summary>Proves how the place directory reads the countries a real posting's place text names, a relocation place line, a country written by name, alias or code, and whether a home country gives free movement to another.</summary>
public sealed class PlaceDirectoryTests
{
    [Theory]
    [InlineData("Remote - Bulgaria", new[] { "BG" })]
    [InlineData("Bulgaria; Poland; Romania", new[] { "BG", "PL", "RO" })]
    [InlineData("Berlin, DE | Germany (REMOTE) | Stuttgart, DE", new[] { "DE" })]
    [InlineData("Edinburgh, Leeds, Manchester, London", new[] { "GB" })]
    [InlineData("Belfast, Northern Ireland, United Kingdom", new[] { "GB" })]
    [InlineData("Barcelona", new[] { "ES" })]
    [InlineData("Remote, United Kingdom", new[] { "GB" })]
    [InlineData("Sydney, New South Wales, Australia", new[] { "AU" })]
    [InlineData("Cluj-Napoca", new[] { "RO" })]
    [InlineData("Kraków", new[] { "PL" })]
    [InlineData("New Mexico", new[] { "US" })]
    [InlineData("Remote", new string[0])]
    [InlineData("Medellin", new string[0])]
    public void CountriesNamedIn_ForARealPostingPlace_ReadsEveryCountryItNamesDirectlyOrThroughACity(string placeText, string[] expected)
    {
        Assert.Equal(expected.Order(), PlaceDirectory.CountriesNamedIn(placeText).Order());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CountriesNamedIn_ForABlankPlace_IsEmpty(string? placeText)
    {
        Assert.Empty(PlaceDirectory.CountriesNamedIn(placeText));
    }

    [Theory]
    [InlineData("Germany", "DE", null)]
    [InlineData(" Holland ", "NL", null)]
    [InlineData("UK", "GB", null)]
    [InlineData("London, United Kingdom", "GB", "London")]
    [InlineData("Glasgow, Scotland", "GB", "Glasgow")]
    [InlineData("Cluj-Napoca, Romania", "RO", "Cluj-Napoca")]
    [InlineData("Barcelona", "ES", "Barcelona")]
    [InlineData("Atlantis", null, "Atlantis")]
    [InlineData("Lisbon, Atlantis", "PT", "Lisbon")]
    [InlineData("Springfield, Atlantis", null, "Springfield")]
    public void ReadEntry_ForARelocationPlaceLine_ReadsTheCountryAndTheCity(string entry, string? expectedCountryIso, string? expectedName)
    {
        Assert.Equal(new RelocationPlace(expectedCountryIso, expectedName), PlaceDirectory.ReadEntry(entry));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ReadEntry_ForABlankLine_IsNull(string? entry)
    {
        Assert.Null(PlaceDirectory.ReadEntry(entry));
    }

    [Theory]
    [InlineData("Germany", "DE")]
    [InlineData("deutschland", "DE")]
    [InlineData("Österreich", "AT")]
    [InlineData("The Netherlands", "NL")]
    [InlineData("Scotland", "GB")]
    [InlineData("DE", "DE")]
    [InlineData("gb", "GB")]
    [InlineData("XX", null)]
    [InlineData("Berlin", null)]
    [InlineData(null, null)]
    public void CountryOf_ForANameAnAliasOrACode_ReadsTheIsoCode(string? name, string? expected)
    {
        Assert.Equal(expected, PlaceDirectory.CountryOf(name));
    }

    [Theory]
    [InlineData("Cluj-Napoca, Romania", "Cluj Napoca", true)]
    [InlineData("Kraków, Poland", "krakow", true)]
    [InlineData("Bernau bei Berlin", "Bern", false)]
    [InlineData("Utrecht", null, false)]
    [InlineData(null, "Utrecht", false)]
    public void NamesPlace_ForAPlaceText_MatchesTheNameAsAWholeWordInAnyCaseWithOrWithoutDiacritics(string? placeText, string? placeName, bool expected)
    {
        Assert.Equal(expected, PlaceDirectory.NamesPlace(placeText, placeName));
    }

    [Theory]
    [InlineData("NL", "DE", true)]
    [InlineData("NL", "NL", true)]
    [InlineData("NL", "CH", true)]
    [InlineData("NL", "NO", true)]
    [InlineData("NL", "GB", false)]
    [InlineData("NL", "AU", false)]
    [InlineData("US", "US", true)]
    [InlineData("US", "DE", false)]
    [InlineData("US", "CA", false)]
    [InlineData("GB", "DE", false)]
    [InlineData(null, "DE", false)]
    public void HasFreeMovement_ForAHomeCountry_IsTrueInsideTheEuropeanAreaForAHomeInItAndOtherwiseOnlyAtHome(string? homeCountryIso, string countryIso, bool expected)
    {
        Assert.Equal(expected, PlaceDirectory.HasFreeMovement(homeCountryIso, countryIso));
    }
}
