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

/// <summary>The geography rules, read against the candidate's profile: the net stays wide, and the only deterministic drops are remote roles restricted to a region the candidate does not accept and onsite roles abroad that give no basis to consider a move.</summary>
public static partial class GeographyRules
{
    /// <summary>The drop reason for an onsite role outside the candidate's region that gives no reason to consider moving for it.</summary>
    public const string OnsiteWithoutBasisReason = "onsite outside your region, no comp, no relocation";

    /// <summary>The drop reason for a remote role open to neither Europe nor the United States.</summary>
    public const string RegionExcludedReason = "remote restricted to a region that excludes Europe and the United States";

    /// <summary>The drop reason for a United States only remote role when those are not accepted.</summary>
    public const string UnitedStatesOnlyReason = "remote restricted to the United States";

    /// <summary>The drop reason for a Europe only remote role when those are not accepted.</summary>
    public const string EuropeOnlyReason = "remote restricted to Europe";

    private static readonly HashSet<string> EuropeanIsoCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AT", "BE", "BG", "CH", "CY", "CZ", "DE", "DK", "EE", "ES", "FI", "FR", "GB", "GR", "HR", "HU", "IE", "IS",
        "IT", "LI", "LT", "LU", "LV", "MT", "NL", "NO", "PL", "PT", "RO", "SE", "SI", "SK", "UK"
    };

    /// <summary>Reads the geography of a posting against the candidate's profile and decides whether it survives.</summary>
    /// <remarks>H3 is raised for a country outside the candidate's region, for the working hours of another region (non-European hours for a candidate in Europe, European hours or CET for one in the United States), for a remote role restricted to an accepted region other than the candidate's own, and for an onsite or hybrid role outside the candidate's region.</remarks>
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
            : EvaluateRemote(candidate, policy, scope, flags);
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

    /// <summary>True when the job's place lies outside the candidate's home region; a job open worldwide, one that names no place, one in the home city, and any job for a candidate with no home region are never outside it.</summary>
    /// <remarks>When the place text tells no region, a recorded country outside the home region counts as outside it, the same test the prefilter raises H3 on. With no country either, a place the text names but the rules do not read as a region counts as outside, so a city or state missing from the place lists (San Jose, Toronto, ON, Sydney) is not let through; a European place missing from them is held back too, which keeps it findable at C. A job recorded in the candidate's home country is never outside, as the prefilter reads it too, even when the home region comes from an accepted remote region the country does not belong to.</remarks>
    public static bool IsOutsideHomeRegion(string? locationText, string? regionText, string? countryIso, string? homeCountryIso, string? homeCity, CandidateRegion homeRegion)
    {
        if (homeRegion == CandidateRegion.None)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(countryIso) && string.Equals(countryIso.Trim(), homeCountryIso?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        RegionScope jobRegion = ReadRegionScope(locationText, regionText, countryIso);

        if (jobRegion == RegionScope.Unspecified)
        {
            if (!string.IsNullOrWhiteSpace(countryIso))
            {
                return !IsCountryInRegion(countryIso, homeRegion);
            }

            string placeText = PlaceText(locationText, regionText);

            return NamesAPlace(placeText) && !IsInHomeCity(homeCity, placeText);
        }

        return homeRegion switch
        {
            CandidateRegion.Europe => jobRegion is RegionScope.UnitedStates or RegionScope.OutsideEuropeAndUnitedStates,
            CandidateRegion.UnitedStates => jobRegion is RegionScope.Europe or RegionScope.OutsideEuropeAndUnitedStates,
            _ => false
        };
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

    private static GeographyVerdict EvaluateRemote(CandidateProfile candidate, RemotePolicyKind policy, RegionScope scope, List<JobFlag> flags)
    {
        if (scope == RegionScope.OutsideEuropeAndUnitedStates)
        {
            return new GeographyVerdict(policy, scope, false, RegionExcludedReason, flags);
        }

        CandidateRegion restrictedTo = scope switch
        {
            RegionScope.Europe => CandidateRegion.Europe,
            RegionScope.UnitedStates => CandidateRegion.UnitedStates,
            _ => CandidateRegion.None
        };

        if (restrictedTo == CandidateRegion.None)
        {
            return new GeographyVerdict(policy, scope, true, null, flags);
        }

        if (!candidate.AcceptsRemoteIn(restrictedTo))
        {
            return new GeographyVerdict(policy, scope, false, restrictedTo == CandidateRegion.Europe ? EuropeOnlyReason : UnitedStatesOnlyReason, flags);
        }

        if (restrictedTo != candidate.HomeRegion)
        {
            Raise(flags, JobFlag.H3);
        }

        return new GeographyVerdict(policy, scope, true, null, flags);
    }

    private static GeographyVerdict EvaluatePresenceRequired(PrefilterInput input, string placeText, RemotePolicyKind policy, RegionScope scope, List<JobFlag> flags)
    {
        Raise(flags, JobFlag.H4);

        CandidateProfile candidate = input.Candidate;

        if (IsInHomeCity(candidate.HomeCity, placeText))
        {
            Raise(flags, JobFlag.HomeCity);

            return new GeographyVerdict(policy, scope, true, null, flags);
        }

        if (IsInsideCandidateRegion(input.CountryIso, scope, candidate))
        {
            return new GeographyVerdict(policy, scope, true, null, flags);
        }

        Raise(flags, JobFlag.H3);

        bool basisToMove = input.HasStatedComp || MentionsRelocationSupport(input.DescriptionText);

        return input.KeepOnsiteWithCompOrRelocation && basisToMove
            ? new GeographyVerdict(policy, scope, true, null, flags)
            : new GeographyVerdict(policy, scope, false, OnsiteWithoutBasisReason, flags);
    }

    /// <summary>True when an onsite or hybrid role sits where the candidate lives: in the home country, in a country of the candidate's region, or, with no country given, in a place the text reads as that region.</summary>
    private static bool IsInsideCandidateRegion(string? countryIso, RegionScope scope, CandidateProfile candidate)
    {
        if (string.IsNullOrWhiteSpace(countryIso))
        {
            return candidate.HomeRegion switch
            {
                CandidateRegion.Europe => scope == RegionScope.Europe,
                CandidateRegion.UnitedStates => scope == RegionScope.UnitedStates,
                _ => false
            };
        }

        return IsCountryInRegion(countryIso, candidate.HomeRegion) || string.Equals(countryIso.Trim(), candidate.HomeCountryIso, StringComparison.OrdinalIgnoreCase);
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

    /// <summary>True when a word is left in the place text once the words that only say how the work is done are taken out, so "Remote" or "Hybrid, office" names no place while "KOHO (CAN)" or "Western States" does.</summary>
    private static bool NamesAPlace(string placeText)
    {
        return PlaceWord().IsMatch(WorkModeWord().Replace(placeText, " "));
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

    [GeneratedRegex(@"\b(relocation|relocate|relocating|visa|work\s*permit|sponsor(ship|ing|s|ed)?|immigration\s*support)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RelocationSupport();

    [GeneratedRegex(@"\b(est|edt|pst|pdt|cst|cdt|mst|mdt|eastern\s*time|pacific\s*time|central\s*time|us\s*(business\s*)?hours|overlap\s*with\s*(the\s*)?(us|pacific|eastern))\b", RegexOptions.IgnoreCase)]
    private static partial Regex NonEuropeanHours();

    [GeneratedRegex(@"\b(cet|cest|central\s*european(\s*summer)?\s*time|(eu|europe|european)\s*(business\s*|working\s*|office\s*)?hours|(eu|europe|european)\s*time\s*zones?|overlap\s*with\s*(the\s*)?(eu|europe|european|cet))\b", RegexOptions.IgnoreCase)]
    private static partial Regex EuropeanHours();
}
