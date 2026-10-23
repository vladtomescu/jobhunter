using JobHunter.Pipeline;

namespace JobHunter.Tests.Pipeline;

/// <summary>Proves where the geography rules place a job from its location, region and country, including the United States state names and "City, ST" places and the guards against reading other places as the United States, which places a candidate accepts for a remote role, and which drop reasons are the geography rules' own.</summary>
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
    [InlineData(null, null, null, true)]
    [InlineData("Remote", null, null, true)]
    [InlineData("remote, Remote", null, null, true)]
    [InlineData("Hybrid, office", null, null, true)]
    [InlineData("Remote - Anywhere", null, "CA", true)]
    [InlineData("Remote", "EMEA", null, true)]
    [InlineData("Remote, Europe", null, "US", true)]
    [InlineData("Remote (CET)", null, null, true)]
    [InlineData("Utrecht", null, null, true)]
    [InlineData("Amsterdam", null, null, true)]
    [InlineData("Remote", null, "NL", true)]
    [InlineData("Remote", null, "nl ", true)]
    [InlineData("Remote - Netherlands", null, null, true)]
    [InlineData("Munich", null, null, true)]
    [InlineData("Remote", null, "DE", true)]
    [InlineData("Lisbon, Portugal", null, null, true)]
    [InlineData("Porto", null, "PT", true)]
    [InlineData("Edinburgh, Leeds, Manchester, London", null, null, true)]
    [InlineData("Belfast, Northern Ireland, United Kingdom", null, null, true)]
    [InlineData("Sydney, New South Wales, Australia", null, null, true)]
    [InlineData("Austin, TX", null, null, false)]
    [InlineData("Remote, USA", null, null, false)]
    [InlineData("Remote", null, "US", false)]
    [InlineData("Toronto", null, "CA", false)]
    [InlineData("Singapore", null, null, false)]
    [InlineData("Paris, France", null, null, false)]
    [InlineData("Dublin, Ireland", null, null, false)]
    [InlineData("Remote - Bulgaria", null, null, false)]
    [InlineData("Remote (LATAM only)", null, null, false)]
    [InlineData("KOHO (CAN)", null, null, false)]
    [InlineData("Medellin", null, null, false)]
    [InlineData("Auckland, New Zealand", null, null, false)]
    [InlineData("Remote U.S.", null, null, false)]
    [InlineData("Remote, U.K.", null, null, true)]
    public void IsPlaceAccepted_ForACandidateInUtrechtWithMixedRelocationPlaces_AcceptsHomeTheRegionAroundItAndEveryCountryOfAPlace(string? locationText, string? regionText, string? countryIso, bool expected)
    {
        Assert.Equal(expected, GeographyRules.IsPlaceAccepted(locationText, regionText, countryIso, CandidateProfiles.NetherlandsJava.Location));
    }

    [Theory]
    [InlineData("Remote - North America", null, true)]
    [InlineData("Remote, Americas", null, true)]
    [InlineData("Austin, TX", null, true)]
    [InlineData("Seattle", null, true)]
    [InlineData("Remote", "US", true)]
    [InlineData("Vancouver", null, true)]
    [InlineData("Raleigh, NC", null, true)]
    [InlineData("Charlotte, North Carolina", null, true)]
    [InlineData("Remote, Europe", null, false)]
    [InlineData("Berlin, Germany", null, false)]
    [InlineData("Remote - Mexico City", null, false)]
    public void IsPlaceAccepted_ForACandidateInTheUnitedStates_AcceptsNorthAmericaTheHomeCountryAndTheRelocationPlaces(string locationText, string? countryIso, bool expected)
    {
        Assert.Equal(expected, GeographyRules.IsPlaceAccepted(locationText, null, countryIso, CandidateProfiles.Austin.Location));
    }

    [Theory]
    [InlineData("Düren", true)]
    [InlineData("Duren - Hybrid", true)]
    [InlineData("Aachen", false)]
    public void IsPlaceAccepted_ForAHomeCityTheDirectoryDoesNotKnow_AcceptsTheHomeCityAlone(string locationText, bool expected)
    {
        CandidateLocation location = new("DE", "Düren", []);

        Assert.Equal(expected, GeographyRules.IsPlaceAccepted(locationText, null, null, location));
    }

    [Theory]
    [InlineData("Berlin", true)]
    [InlineData("Remote", true)]
    [InlineData("Austin, TX", false)]
    [InlineData("Remote - Anywhere", true)]
    public void IsPlaceAccepted_ForACandidateWhoSetOnlyRelocationPlaces_JudgesThePlaceAgainstThem(string locationText, bool expected)
    {
        CandidateLocation location = new(null, null, ["Germany"]);

        Assert.Equal(expected, GeographyRules.IsPlaceAccepted(locationText, null, null, location));
    }

    [Theory]
    [InlineData("Austin, TX", null)]
    [InlineData("Singapore", null)]
    [InlineData("Toronto", "CA")]
    [InlineData("Remote (LATAM only)", null)]
    [InlineData("KOHO (CAN)", null)]
    public void IsPlaceAccepted_ForACandidateWhoSetNoPlaces_AcceptsEveryPlace(string locationText, string? countryIso)
    {
        CandidateLocation location = CandidateLocation.FromSettings(JobHunter.Domain.Settings.CreateDefault());

        Assert.False(location.JudgesPlaces);
        Assert.True(GeographyRules.IsPlaceAccepted(locationText, null, countryIso, location));
    }

    [Theory]
    [InlineData(GeographyRules.OutsidePlacesReason)]
    [InlineData(GeographyRules.NoWorkVisaReason)]
    [InlineData(GeographyRules.RegionExcludedReason)]
    [InlineData("onsite outside your region, no comp, no relocation")]
    [InlineData("remote restricted to a region that excludes Europe and the United States")]
    [InlineData("remote restricted to the United States")]
    [InlineData("remote restricted to Europe")]
    public void IsGeographyDropReason_ForAReasonTheGeographyRulesGiveOrGaveEarlier_IsTrue(string reason)
    {
        Assert.True(GeographyRules.IsGeographyDropReason(reason));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(Prefilter.AgeReason)]
    [InlineData(Prefilter.AgencyReason)]
    [InlineData(Prefilter.LanguageReason)]
    [InlineData(Prefilter.NoIncludeTermReason)]
    [InlineData("title excluded: frontend")]
    public void IsGeographyDropReason_ForAnyOtherReason_IsFalse(string? reason)
    {
        Assert.False(GeographyRules.IsGeographyDropReason(reason));
    }
}
