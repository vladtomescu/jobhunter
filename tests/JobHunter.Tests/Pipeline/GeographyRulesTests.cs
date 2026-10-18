using JobHunter.Pipeline;

namespace JobHunter.Tests.Pipeline;

/// <summary>Proves where the geography rules place a job from its location, region and country, including the United States state names and "City, ST" places and the guards against reading other places as the United States, and when that place lies outside the candidate's home region.</summary>
public sealed class GeographyRulesTests
{
    [Theory]
    [InlineData("Raleigh, NC", null, null)]
    [InlineData("Bellevue, WA (Remote)", null, null)]
    [InlineData("Plano, TX - Remote", null, null)]
    [InlineData("Remote", "Raleigh, NC", null)]
    [InlineData("Raleigh, NC", null, "US")]
    [InlineData("Albany, New York", null, null)]
    [InlineData("Newark, New Jersey", null, null)]
    [InlineData("Santa Fe, New Mexico", null, null)]
    [InlineData("Concord, New Hampshire", null, null)]
    [InlineData("Charlotte, North Carolina", null, null)]
    [InlineData("Columbia, South Carolina", null, null)]
    [InlineData("Fargo, North Dakota", null, null)]
    [InlineData("Sioux Falls, South Dakota", null, null)]
    [InlineData("Providence, Rhode Island", null, null)]
    [InlineData("Charleston, West Virginia", null, null)]
    [InlineData("Savannah, Georgia", null, null)]
    [InlineData("Washington, DC", null, null)]
    [InlineData("Washington D.C.", null, null)]
    [InlineData("Washington State", null, null)]
    [InlineData("Redmond, Washington", null, null)]
    [InlineData("Remote - North America", null, null)]
    [InlineData("Raleigh, NC 27601", null, null)]
    [InlineData("Spokane, WA Remote", null, null)]
    public void ReadRegionScope_ForAUnitedStatesPlace_ReadsTheUnitedStates(string? locationText, string? regionText, string? countryIso)
    {
        Assert.Equal(RegionScope.UnitedStates, GeographyRules.ReadRegionScope(locationText, regionText, countryIso));
    }

    [Theory]
    [InlineData("Toronto, CA", "CA", RegionScope.Unspecified)]
    [InlineData("Bengaluru, IN", "IN", RegionScope.Unspecified)]
    [InlineData("Hamburg, DE", null, RegionScope.Europe)]
    [InlineData("Raleigh, nc", null, RegionScope.Unspecified)]
    [InlineData("NC office", null, RegionScope.Unspecified)]
    [InlineData("Washington", null, RegionScope.Unspecified)]
    [InlineData("Tbilisi, Georgia", null, RegionScope.Unspecified)]
    [InlineData("Remote - Latin America", null, RegionScope.OutsideEuropeAndUnitedStates)]
    [InlineData("South America", null, RegionScope.OutsideEuropeAndUnitedStates)]
    [InlineData("Central America", null, RegionScope.OutsideEuropeAndUnitedStates)]
    [InlineData("Mexico City, Mexico", null, RegionScope.OutsideEuropeAndUnitedStates)]
    [InlineData("Toronto, ON, Canada", null, RegionScope.OutsideEuropeAndUnitedStates)]
    public void ReadRegionScope_ForAPlaceThatOnlyLooksAmerican_DoesNotReadTheUnitedStates(string? locationText, string? countryIso, RegionScope expected)
    {
        Assert.Equal(expected, GeographyRules.ReadRegionScope(locationText, null, countryIso));
    }

    [Theory]
    [InlineData("Austin, TX", null, CandidateRegion.Europe, true)]
    [InlineData("Singapore", null, CandidateRegion.Europe, true)]
    [InlineData("Berlin, Germany", null, CandidateRegion.Europe, false)]
    [InlineData("Remote - Anywhere", null, CandidateRegion.Europe, false)]
    [InlineData(null, null, CandidateRegion.Europe, false)]
    [InlineData("Remote, Europe", "US", CandidateRegion.Europe, false)]
    [InlineData("Berlin, Germany", null, CandidateRegion.UnitedStates, true)]
    [InlineData("Austin, TX", null, CandidateRegion.UnitedStates, false)]
    [InlineData("Austin, TX", null, CandidateRegion.None, false)]
    [InlineData("Toronto", "CA", CandidateRegion.Europe, true)]
    [InlineData("Bengaluru", "IN", CandidateRegion.UnitedStates, true)]
    [InlineData("Hamburg", "DE", CandidateRegion.Europe, false)]
    [InlineData("Toronto", "CA", CandidateRegion.None, false)]
    [InlineData("Remote - Anywhere", "CA", CandidateRegion.Europe, false)]
    [InlineData("Remote U.S.", null, CandidateRegion.Europe, true)]
    [InlineData("San Jose", null, CandidateRegion.Europe, true)]
    [InlineData("Toronto, ON", null, CandidateRegion.Europe, true)]
    [InlineData("BC, AB, ON, MB", null, CandidateRegion.Europe, true)]
    [InlineData("KOHO (CAN)", null, CandidateRegion.Europe, true)]
    [InlineData("Western States or Midwest", null, CandidateRegion.Europe, true)]
    [InlineData("Medellin", null, CandidateRegion.Europe, true)]
    [InlineData("Remote", null, CandidateRegion.Europe, false)]
    [InlineData("remote, Remote", null, CandidateRegion.Europe, false)]
    [InlineData("Hybrid, office", null, CandidateRegion.Europe, false)]
    public void IsOutsideHomeRegion_ForAJobPlaceAndAHomeRegion_IsTrueWhenThePlaceLiesOutsideTheHomeRegion(string? locationText, string? countryIso, CandidateRegion homeRegion, bool expected)
    {
        Assert.Equal(expected, GeographyRules.IsOutsideHomeRegion(locationText, null, countryIso, null, null, homeRegion));
    }

    [Theory]
    [InlineData("Belgrade", "RS", "RS", CandidateRegion.Europe)]
    [InlineData("Toronto", "CA", "CA", CandidateRegion.UnitedStates)]
    [InlineData("Toronto", "ca ", "CA", CandidateRegion.UnitedStates)]
    public void IsOutsideHomeRegion_ForAJobInTheHomeCountryOutsideTheHomeRegion_IsFalse(string locationText, string countryIso, string homeCountryIso, CandidateRegion homeRegion)
    {
        Assert.False(GeographyRules.IsOutsideHomeRegion(locationText, null, countryIso, homeCountryIso, null, homeRegion));
    }

    [Theory]
    [InlineData("Timisoara")]
    [InlineData("Timișoara - Hybrid")]
    public void IsOutsideHomeRegion_ForAPlaceInTheHomeCityTheListsDoNotKnow_IsFalse(string locationText)
    {
        Assert.False(GeographyRules.IsOutsideHomeRegion(locationText, null, null, null, "Timișoara", CandidateRegion.Europe));
    }
}
