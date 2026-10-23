namespace JobHunter.Pipeline;

/// <summary>Where the candidate lives and would move to, read off the settings: the home country, city and region, and the relocation places, which the geography rules judge a posting's place against.</summary>
public sealed class CandidateLocation
{
    /// <summary>Builds the location and reads each relocation place once.</summary>
    public CandidateLocation(string? homeCountryIso, string? homeCity, IEnumerable<string> relocationPlaces)
    {
        ArgumentNullException.ThrowIfNull(relocationPlaces);

        HomeCountryIso = string.IsNullOrWhiteSpace(homeCountryIso) ? null : homeCountryIso.Trim().ToUpperInvariant();
        HomeCity = homeCity?.Trim() ?? string.Empty;
        HomeRegion = ReadHomeRegion(HomeCountryIso);
        RelocationPlaces = [.. relocationPlaces.Select(PlaceDirectory.ReadEntry).OfType<RelocationPlace>()];
    }

    /// <summary>ISO 3166-1 alpha-2 code of the candidate's country, upper case; null when not set.</summary>
    public string? HomeCountryIso { get; }

    /// <summary>The city the candidate lives in, trimmed; empty when not set.</summary>
    public string HomeCity { get; }

    /// <summary>The region of the home country when it lies in Europe or the United States, otherwise none.</summary>
    public CandidateRegion HomeRegion { get; }

    /// <summary>The places the candidate would move to, in the order the settings list them.</summary>
    public IReadOnlyList<RelocationPlace> RelocationPlaces { get; }

    /// <summary>False for a candidate who set no home country, home city or relocation place: the geography rules then keep every place, since they have nothing to judge it against.</summary>
    public bool JudgesPlaces => HomeCountryIso is not null || HomeCity.Length > 0 || RelocationPlaces.Count > 0;

    /// <summary>Reads the location off the settings row, without compiling the rest of the candidate profile.</summary>
    /// <remarks>The settings type is written qualified because the JobHunter.Settings namespace shadows the plain name.</remarks>
    public static CandidateLocation FromSettings(Domain.Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new CandidateLocation(settings.HomeCountryIso, settings.HomeCity, LiteralTermPattern.ReadTerms(settings.RelocationPlaces, '\n'));
    }

    private static CandidateRegion ReadHomeRegion(string? homeCountryIso)
    {
        if (GeographyRules.IsEuropeanCountry(homeCountryIso))
        {
            return CandidateRegion.Europe;
        }

        return GeographyRules.IsUnitedStatesCountry(homeCountryIso) ? CandidateRegion.UnitedStates : CandidateRegion.None;
    }
}
