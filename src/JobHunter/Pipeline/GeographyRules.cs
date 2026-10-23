using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using JobHunter.Domain;
using JobHunter.Sources;

namespace JobHunter.Pipeline;

/// <summary>Where a role can be done from, as far as the posting says.</summary>
public enum RemotePolicyKind
{
    Remote,
    Hybrid,
    Onsite,
    Unspecified
}

/// <summary>The part of the world a remote posting is open to.</summary>
public enum RegionScope
{
    Unspecified,
    Worldwide,
    Europe,
    UnitedStates,
    OutsideEuropeAndUnitedStates
}

/// <summary>What the geography rules make of a posting: the policy and scope they read, whether the job survives, and the flags they raise.</summary>
public sealed record GeographyVerdict(RemotePolicyKind Policy, RegionScope Scope, bool Keeps, string? DropReason, IReadOnlyList<JobFlag> Flags);

/// <summary>The geography rules, read against the candidate's location: a job in the home city always stays, an onsite or hybrid role stays only in a relocation place, a remote role restricted to a region that holds neither the home country nor a relocation place is dropped, and every other remote role goes to the model, whose reading the classifier holds it to.</summary>
public static partial class GeographyRules
{
    /// <summary>The drop reason for an onsite or hybrid role outside the home city and every relocation place.</summary>
    public const string OutsidePlacesReason = "onsite outside your home city and relocation places";

    /// <summary>The drop reason for a role kept only by relocation places where the candidate needs a work visa, when the posting mentions neither relocation, a visa nor sponsorship.</summary>
    public const string NoWorkVisaReason = "needs a work visa and mentions no relocation or sponsorship";

    /// <summary>The drop reason for a remote role restricted to a region that holds neither the home country nor a relocation place.</summary>
    public const string RegionExcludedReason = "remote restricted to a region outside your home and relocation places";

    /// <summary>The drop reasons earlier versions of these rules stored, which a change of the candidate's places may lift as well.</summary>
    private static readonly string[] EarlierDropReasons =
    [
        "onsite outside your region, no comp, no relocation",
        "remote restricted to a region that excludes Europe and the United States",
        "remote restricted to the United States",
        "remote restricted to Europe"
    ];

    /// <summary>The words that offer relocation, a visa or sponsorship, matched without regard to case; the inbox filter runs the same pattern in the database.</summary>
    public const string RelocationSupportPattern = @"\b(relocation|relocate|relocating|visa|work\s*permit|sponsor(ship|ing|s|ed)?|immigration\s*support)\b";

    private static readonly HashSet<string> EuropeanIsoCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AT", "BE", "BG", "CH", "CY", "CZ", "DE", "DK", "EE", "ES", "FI", "FR", "GB", "GR", "HR", "HU", "IE", "IS",
        "IT", "LI", "LT", "LU", "LV", "MT", "NL", "NO", "PL", "PT", "RO", "SE", "SI", "SK", "UK"
    };

    /// <summary>Reads the geography of a posting against the candidate's profile and decides whether it survives.</summary>
    /// <remarks>H3 is raised for a country outside the candidate's region, for the working hours of another region (non-European hours for a candidate in Europe, European hours or CET for one in the United States), for a remote role restricted to a region other than the candidate's own, and for an onsite or hybrid role outside the candidate's region.</remarks>
    public static GeographyVerdict Evaluate(PrefilterInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        CandidateProfile candidate = input.Candidate;
        string placeText = PlaceText(input.LocationText, input.RegionText);
        RemotePolicyKind policy = InferPolicy(input, placeText);
        RegionScope scope = ReadRegionScope(input.LocationText, input.RegionText, input.CountryIso);
        List<JobFlag> flags = [];

        bool countryOutsideRegion = !string.IsNullOrWhiteSpace(input.CountryIso) && !IsCountryInRegion(input.CountryIso, candidate.HomeRegion);
        if (countryOutsideRegion || RequiresHoursOutside(input.DescriptionText, candidate.HomeRegion) || RequiresHoursOutside(placeText, candidate.HomeRegion))
        {
            Raise(flags, JobFlag.H3);
        }

        return policy is RemotePolicyKind.Onsite or RemotePolicyKind.Hybrid
            ? EvaluatePresenceRequired(input, placeText, policy, scope, flags)
            : EvaluateRemote(input, placeText, policy, scope, flags);
    }

    /// <summary>How well the place of a posting suits the candidate, lower being better: 0 in the home city, 1 kept by these rules, 2 dropped by them.</summary>
    public static int PlaceRank(PrefilterInput input)
    {
        GeographyVerdict verdict = Evaluate(input);

        return verdict.Flags.Contains(JobFlag.HomeCity) ? 0 : verdict.Keeps ? 1 : 2;
    }

    /// <summary>True when the country belongs to the European Union, the European Economic Area, the United Kingdom or Switzerland.</summary>
    public static bool IsEuropeanCountry(string? countryIso)
    {
        return !string.IsNullOrWhiteSpace(countryIso) && EuropeanIsoCodes.Contains(countryIso.Trim());
    }

    /// <summary>True when the country is the United States.</summary>
    public static bool IsUnitedStatesCountry(string? countryIso)
    {
        return string.Equals(countryIso?.Trim(), "US", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The region a posting places the job in, as the prefilter reads it: the region its location and region text name, else a United States "City, ST" place when no country is given, else the region of its country.</summary>
    /// <remarks>
    /// Named places win over the country, so a posting whose text opens it to Europe or worldwide is not read as the region of its country.
    /// A two-letter state code counts only in the "City, ST" shape, upper case, optionally followed by a ZIP code, at the end of a place, before a separator or before a remote, hybrid or onsite word, and only when the posting gives no country: codes such as CA, DE or IN are also country codes, so a recorded country decides instead, while "Toronto, CA" with no country still reads as the United States.
    /// Washington counts as Washington, DC, Washington state or "City, Washington", never alone; Georgia reads as the state unless a Georgian city (Tbilisi, Batumi, Kutaisi) comes before it, so "Georgia" alone for the country is read as the state.
    /// Latin, South and Central America are not read as the United States; North America still is.
    /// </remarks>
    public static RegionScope ReadRegionScope(string? locationText, string? regionText, string? countryIso)
    {
        RegionScope fromText = ReadScope(PlaceText(locationText, regionText));

        if (fromText != RegionScope.Unspecified)
        {
            return fromText;
        }

        if (string.IsNullOrWhiteSpace(countryIso) && (NamesUnitedStatesStateCode(locationText) || NamesUnitedStatesStateCode(regionText)))
        {
            return RegionScope.UnitedStates;
        }

        return ReadScopeFromCountry(countryIso);
    }

    /// <summary>True when a remote role in this place is open to the candidate: no place is named, the place is worldwide or a region around the home country, the home city or country, or a relocation place, where a city counts for its whole country since living there meets a rule to reside in that country.</summary>
    /// <remarks>A place the text names but the directory does not know is not accepted, so a city missing from it is not let through. The classifier holds a scored job whose place is not accepted to D unless the model read the posting as open to the candidate's region, so a label naming an office abroad costs nothing when the text opens the role to the candidate.</remarks>
    public static bool IsPlaceAccepted(string? locationText, string? regionText, string? countryIso, CandidateLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);

        string placeText = PlaceText(locationText, regionText);

        return !location.JudgesPlaces || IsOpenToHome(placeText, countryIso, location) || ListedPlacesIn(placeText, countryIso, location, cityCountsForItsCountry: true).Count > 0;
    }

    /// <summary>True when the drop reason is one the geography rules give, now or in an earlier version, so a change of the candidate's places may lift it.</summary>
    public static bool IsGeographyDropReason(string? reason)
    {
        return reason is OutsidePlacesReason or NoWorkVisaReason or RegionExcludedReason || EarlierDropReasons.Contains(reason);
    }

    /// <summary>True when the place text names the candidate's home city as a whole word, in any case and with or without diacritics; the one test the prefilter uses to raise <see cref="JobFlag.HomeCity"/>, reused as-is by the candidate flag recompute so both agree on what "home" means.</summary>
    public static bool IsInHomeCity(string? homeCity, string? placeText)
    {
        if (string.IsNullOrWhiteSpace(homeCity) || string.IsNullOrWhiteSpace(placeText))
        {
            return false;
        }

        string cityPattern = LiteralTermPattern.WholeWord(WithoutDiacritics(homeCity));

        return Regex.IsMatch(WithoutDiacritics(placeText), cityPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>True when the posting offers relocation, a visa or sponsorship, which is a basis to consider an onsite role abroad.</summary>
    public static bool MentionsRelocationSupport(string text)
    {
        return !string.IsNullOrWhiteSpace(text) && RelocationSupport().IsMatch(text);
    }

    /// <summary>True when the posting asks for the working hours of a region other than the candidate's: non-European hours for Europe, European hours or CET for the United States.</summary>
    public static bool RequiresHoursOutside(string text, CandidateRegion region)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return region switch
        {
            CandidateRegion.Europe => NonEuropeanHours().IsMatch(text),
            CandidateRegion.UnitedStates => EuropeanHours().IsMatch(text),
            _ => false
        };
    }

    private static GeographyVerdict EvaluateRemote(PrefilterInput input, string placeText, RemotePolicyKind policy, RegionScope scope, List<JobFlag> flags)
    {
        CandidateLocation location = input.Candidate.Location;

        if (scope == RegionScope.OutsideEuropeAndUnitedStates)
        {
            return IsOpenToHome(placeText, input.CountryIso, location)
                ? new GeographyVerdict(policy, scope, true, null, flags)
                : JudgeRelocationPlaces(input, placeText, policy, scope, flags, cityCountsForItsCountry: true, RegionExcludedReason);
        }

        CandidateRegion restrictedTo = scope switch
        {
            RegionScope.Europe => CandidateRegion.Europe,
            RegionScope.UnitedStates => CandidateRegion.UnitedStates,
            _ => CandidateRegion.None
        };

        if (restrictedTo != CandidateRegion.None && restrictedTo != location.HomeRegion)
        {
            Raise(flags, JobFlag.H3);
        }

        return new GeographyVerdict(policy, scope, true, null, flags);
    }

    private static GeographyVerdict EvaluatePresenceRequired(PrefilterInput input, string placeText, RemotePolicyKind policy, RegionScope scope, List<JobFlag> flags)
    {
        Raise(flags, JobFlag.H4);

        CandidateLocation location = input.Candidate.Location;

        if (IsInHomeCity(location.HomeCity, placeText))
        {
            Raise(flags, JobFlag.HomeCity);

            return new GeographyVerdict(policy, scope, true, null, flags);
        }

        if (!IsInsideCandidateRegion(input.CountryIso, scope, location))
        {
            Raise(flags, JobFlag.H3);
        }

        if (string.IsNullOrWhiteSpace(input.CountryIso) && !NamesAPlace(placeText))
        {
            return new GeographyVerdict(policy, scope, true, null, flags);
        }

        return JudgeRelocationPlaces(input, placeText, policy, scope, flags, cityCountsForItsCountry: false, OutsidePlacesReason);
    }

    /// <summary>Keeps a job in a relocation place and drops it with the reason given when it names none; a job kept only by places where the candidate needs a work visa is dropped too unless the posting mentions relocation, a visa or sponsorship. A candidate who set no places keeps every job, and one who set no home country is never held to a visa, since the rules cannot tell where they may work.</summary>
    private static GeographyVerdict JudgeRelocationPlaces(PrefilterInput input, string placeText, RemotePolicyKind policy, RegionScope scope, List<JobFlag> flags, bool cityCountsForItsCountry, string outsideReason)
    {
        CandidateLocation location = input.Candidate.Location;

        if (!location.JudgesPlaces)
        {
            return new GeographyVerdict(policy, scope, true, null, flags);
        }

        IReadOnlyList<RelocationPlace> listed = ListedPlacesIn(placeText, input.CountryIso, location, cityCountsForItsCountry);

        if (listed.Count == 0)
        {
            return new GeographyVerdict(policy, scope, false, outsideReason, flags);
        }

        bool needsWorkVisa = location.HomeCountryIso is not null && listed.All(place => place.CountryIso is string countryIso && !PlaceDirectory.HasFreeMovement(location.HomeCountryIso, countryIso));

        return needsWorkVisa && !MentionsRelocationSupport(input.DescriptionText)
            ? new GeographyVerdict(policy, scope, false, NoWorkVisaReason, flags)
            : new GeographyVerdict(policy, scope, true, null, flags);
    }

    /// <summary>True when the place leaves the candidate where they live: no place named, worldwide, a region around the home country, the home city, or the home country by its recorded code or its name.</summary>
    private static bool IsOpenToHome(string placeText, string? countryIso, CandidateLocation location)
    {
        if (string.IsNullOrWhiteSpace(countryIso) && !NamesAPlace(placeText))
        {
            return true;
        }

        if (Worldwide().IsMatch(placeText) || IsRegionAroundHome(placeText, location.HomeRegion) || IsInHomeCity(location.HomeCity, placeText))
        {
            return true;
        }

        return location.HomeCountryIso is string home && CountriesIn(placeText, countryIso).Contains(home);
    }

    /// <summary>The countries a place names: the ones the directory reads by name or city, the recorded country, and the United States for a state, a "City, ST" place with no country given, or another place the region rules read as the United States.</summary>
    private static HashSet<string> CountriesIn(string placeText, string? countryIso)
    {
        HashSet<string> countries = new(PlaceDirectory.CountriesNamedIn(placeText), StringComparer.OrdinalIgnoreCase);

        if (PlaceDirectory.CountryOf(countryIso) is string recordedCountry)
        {
            countries.Add(recordedCountry);
        }

        if (UnitedStatesPlace().IsMatch(placeText) || (string.IsNullOrWhiteSpace(countryIso) && NamesUnitedStatesStateCode(placeText)))
        {
            countries.Add("US");
        }

        return countries;
    }

    private static bool IsRegionAroundHome(string placeText, CandidateRegion homeRegion)
    {
        return homeRegion switch
        {
            CandidateRegion.Europe => EuropeanRegion().IsMatch(placeText),
            CandidateRegion.UnitedStates => NorthAmericanRegion().IsMatch(placeText),
            _ => false
        };
    }

    /// <summary>The relocation places a place text or a recorded country names: a country place by its code, its name or one of its cities, a city place by its name, and with <paramref name="cityCountsForItsCountry"/> also by its country.</summary>
    private static IReadOnlyList<RelocationPlace> ListedPlacesIn(string placeText, string? countryIso, CandidateLocation location, bool cityCountsForItsCountry)
    {
        if (location.RelocationPlaces.Count == 0)
        {
            return [];
        }

        HashSet<string> countries = CountriesIn(placeText, countryIso);

        bool InCountry(string? placeCountry) => placeCountry is not null && countries.Contains(placeCountry);

        return [.. location.RelocationPlaces.Where(place => place.Name is null ? InCountry(place.CountryIso) : PlaceDirectory.NamesPlace(placeText, place.Name) || (cityCountsForItsCountry && InCountry(place.CountryIso)))];
    }

    /// <summary>True when an onsite or hybrid role sits where the candidate lives: in the home country, in a country of the candidate's region, or, with no country given, in a place the text reads as that region.</summary>
    private static bool IsInsideCandidateRegion(string? countryIso, RegionScope scope, CandidateLocation location)
    {
        if (string.IsNullOrWhiteSpace(countryIso))
        {
            return location.HomeRegion switch
            {
                CandidateRegion.Europe => scope == RegionScope.Europe,
                CandidateRegion.UnitedStates => scope == RegionScope.UnitedStates,
                _ => false
            };
        }

        return IsCountryInRegion(countryIso, location.HomeRegion) || string.Equals(countryIso.Trim(), location.HomeCountryIso, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCountryInRegion(string? countryIso, CandidateRegion region)
    {
        return region switch
        {
            CandidateRegion.Europe => IsEuropeanCountry(countryIso),
            CandidateRegion.UnitedStates => IsUnitedStatesCountry(countryIso),
            _ => false
        };
    }

    private static RemotePolicyKind InferPolicy(PrefilterInput input, string placeText)
    {
        if (input.IsRemoteFromSource == true || input.Sources.Any(source => source is JobSourceKind.RemoteOk or JobSourceKind.WeWorkRemotely))
        {
            return RemotePolicyKind.Remote;
        }

        if (Hybrid().IsMatch(placeText))
        {
            return RemotePolicyKind.Hybrid;
        }

        if (Remote().IsMatch(placeText))
        {
            return RemotePolicyKind.Remote;
        }

        if (input.IsRemoteFromSource == false || Onsite().IsMatch(placeText))
        {
            return RemotePolicyKind.Onsite;
        }

        return ReadPolicyFromDescription(input.DescriptionText);
    }

    private static RemotePolicyKind ReadPolicyFromDescription(string description)
    {
        if (Hybrid().IsMatch(description))
        {
            return RemotePolicyKind.Hybrid;
        }

        if (Remote().IsMatch(description))
        {
            return RemotePolicyKind.Remote;
        }

        return Onsite().IsMatch(description) ? RemotePolicyKind.Onsite : RemotePolicyKind.Unspecified;
    }

    private static string PlaceText(string? locationText, string? regionText)
    {
        return string.Join(' ', new[] { locationText, regionText }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    /// <summary>True when a word is left in the place text once the words that only say how the work is done are taken out, or the text names a country the directory knows, so "Remote" or "Hybrid, office" names no place while "KOHO (CAN)", "Western States" or "Remote U.S." does.</summary>
    private static bool NamesAPlace(string placeText)
    {
        return PlaceWord().IsMatch(WorkModeWord().Replace(placeText, " ")) || PlaceDirectory.CountriesNamedIn(placeText).Count > 0;
    }

    private static bool NamesUnitedStatesStateCode(string? place)
    {
        return !string.IsNullOrWhiteSpace(place) && UnitedStatesStateCode().IsMatch(place);
    }

    private static RegionScope ReadScope(string placeText)
    {
        if (string.IsNullOrWhiteSpace(placeText))
        {
            return RegionScope.Unspecified;
        }

        if (EuropeanPlace().IsMatch(placeText))
        {
            return RegionScope.Europe;
        }

        if (UnitedStatesPlace().IsMatch(placeText))
        {
            return RegionScope.UnitedStates;
        }

        if (OtherRegion().IsMatch(placeText))
        {
            return RegionScope.OutsideEuropeAndUnitedStates;
        }

        return Worldwide().IsMatch(placeText) ? RegionScope.Worldwide : RegionScope.Unspecified;
    }

    private static RegionScope ReadScopeFromCountry(string? countryIso)
    {
        if (IsEuropeanCountry(countryIso))
        {
            return RegionScope.Europe;
        }

        return IsUnitedStatesCountry(countryIso) ? RegionScope.UnitedStates : RegionScope.Unspecified;
    }

    private static void Raise(List<JobFlag> flags, JobFlag flag)
    {
        if (!flags.Contains(flag))
        {
            flags.Add(flag);
        }
    }

    /// <summary>The text with every combining mark taken off its letters, so that a city written with diacritics and one written without them read alike.</summary>
    private static string WithoutDiacritics(string text)
    {
        string decomposed = text.Normalize(NormalizationForm.FormD);
        StringBuilder bare = new(decomposed.Length);

        foreach (char character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                bare.Append(character);
            }
        }

        return bare.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"\bhybrid\b", RegexOptions.IgnoreCase)]
    private static partial Regex Hybrid();

    [GeneratedRegex(@"\b(remote|anywhere|worldwide|work\s*from\s*home|distributed\s*team)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Remote();

    [GeneratedRegex(@"\b(on[\s\-]?site|in[\s\-]?office|in[\s\-]?person|office[\s\-]based|relocation\s*required)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Onsite();

    [GeneratedRegex(@"\b(worldwide|anywhere|global(ly)?|any\s*country|any\s*time\s*zone)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Worldwide();

    [GeneratedRegex(@"\b(remote|hybrid|on[\s\-]?site|in[\s\-]?office|in[\s\-]?person|office|offices|hq|headquarters|home|based|first|flexible|distributed|team|wfh|work|from|multiple|various|locations?|full[\s\-]?time|part[\s\-]?time|only|or|and|in|the|n/?a|tbd)\b", RegexOptions.IgnoreCase)]
    private static partial Regex WorkModeWord();

    [GeneratedRegex(@"\p{L}{2,}")]
    private static partial Regex PlaceWord();

    [GeneratedRegex(@"\b(europe|european|emea|eu|eea|uk|united\s*kingdom|england|scotland|wales|ireland|germany|deutschland|france|spain|portugal|italy|netherlands|holland|belgium|luxembourg|austria|switzerland|denmark|sweden|norway|finland|iceland|poland|czech(ia)?|slovakia|slovenia|hungary|bulgaria|croatia|greece|cyprus|malta|estonia|latvia|lithuania|romania|românia|london|manchester|edinburgh|dublin|berlin|munich|hamburg|frankfurt|cologne|paris|lyon|madrid|barcelona|valencia|lisbon|porto|milan|rome|turin|amsterdam|rotterdam|utrecht|brussels|antwerp|vienna|zurich|geneva|basel|copenhagen|stockholm|gothenburg|oslo|helsinki|reykjavik|warsaw|krakow|wroclaw|gdansk|prague|brno|bratislava|ljubljana|budapest|sofia|zagreb|athens|tallinn|riga|vilnius|bucharest|cluj([\s\-]*napoca)?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex EuropeanPlace();

    [GeneratedRegex(@"(?<!\w)u\.s\.(a\.?)?(?!\w)|\b(usa|us|united\s*states|(?<!\b(latin|south|central)[\s\-]*)america(n)?|nyc)\b|\b(san\s*francisco|bay\s*area|los\s*angeles|seattle|austin|boston|chicago|denver|atlanta|miami|dallas|houston|portland|philadelphia|phoenix|san\s*diego|washington\s*,?\s*d\.?\s*c|washington\s+state)\b|,\s*washington\b|\b(alabama|alaska|arizona|arkansas|california|colorado|connecticut|delaware|florida|(?<!\b(tbilisi|batumi|kutaisi)\W*)georgia|hawaii|idaho|illinois|indiana|iowa|kansas|kentucky|louisiana|maine|maryland|massachusetts|michigan|minnesota|mississippi|missouri|montana|nebraska|nevada|new\s*hampshire|new\s*jersey|new\s*mexico|new\s*york|(north|south)\s*carolina|(north|south)\s*dakota|ohio|oklahoma|oregon|pennsylvania|rhode\s*island|tennessee|texas|utah|vermont|(west\s*)?virginia|wisconsin|wyoming)\b", RegexOptions.IgnoreCase)]
    private static partial Regex UnitedStatesPlace();

    [GeneratedRegex(@"\p{L}\.?\s*,\s*(AK|AL|AR|AZ|CA|CO|CT|DC|DE|FL|GA|HI|IA|ID|IL|IN|KS|KY|LA|MA|MD|ME|MI|MN|MO|MS|MT|NC|ND|NE|NH|NJ|NM|NV|NY|OH|OK|OR|PA|RI|SC|SD|TN|TX|UT|VA|VT|WA|WI|WV|WY)(\s+\d{5}(-\d{4})?)?(?=\s*($|[,;|/()\-–—])|\s+(?i:remote|hybrid|on[\s\-]?site)\b)")]
    private static partial Regex UnitedStatesStateCode();

    [GeneratedRegex(@"\b(latam|latin\s*america|south\s*america|central\s*america|apac|asia([\s\-]*pacific)?|anz|australia|new\s*zealand|india|africa|mena|middle\s*east|canada|brazil|brasil|mexico|argentina|chile|colombia|peru|uruguay|philippines|indonesia|vietnam|thailand|malaysia|singapore|china|japan|korea|israel|uae|dubai|egypt|nigeria|kenya|south\s*africa|pakistan|bangladesh|sri\s*lanka)\b", RegexOptions.IgnoreCase)]
    private static partial Regex OtherRegion();

    [GeneratedRegex(@"\b(europe|european|emea|eu|eea|cet|cest|central\s*european(\s*summer)?\s*time)\b", RegexOptions.IgnoreCase)]
    private static partial Regex EuropeanRegion();

    [GeneratedRegex(@"\b(north\s*america|americas)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NorthAmericanRegion();

    [GeneratedRegex(RelocationSupportPattern, RegexOptions.IgnoreCase)]
    private static partial Regex RelocationSupport();

    [GeneratedRegex(@"\b(est|edt|pst|pdt|cst|cdt|mst|mdt|eastern\s*time|pacific\s*time|central\s*time|us\s*(business\s*)?hours|overlap\s*with\s*(the\s*)?(us|pacific|eastern))\b", RegexOptions.IgnoreCase)]
    private static partial Regex NonEuropeanHours();

    [GeneratedRegex(@"\b(cet|cest|central\s*european(\s*summer)?\s*time|(eu|europe|european)\s*(business\s*|working\s*|office\s*)?hours|(eu|europe|european)\s*time\s*zones?|overlap\s*with\s*(the\s*)?(eu|europe|european|cet))\b", RegexOptions.IgnoreCase)]
    private static partial Regex EuropeanHours();
}
