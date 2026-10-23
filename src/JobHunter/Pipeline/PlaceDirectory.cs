using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JobHunter.Pipeline;

/// <summary>A place the candidate would move to, read off one line of the settings: a country (<see cref="CountryIso"/> alone), a city in a known country (both), or a name the directory does not know, matched as written (<see cref="Name"/> alone).</summary>
public sealed record RelocationPlace(string? CountryIso, string? Name);

/// <summary>The countries and cities the geography rules can recognize in a place text: country names and their common aliases, and the cities job postings name most, each mapped to its ISO 3166-1 alpha-2 code.</summary>
/// <remarks>Matching ignores case and diacritics and takes whole words only. Longer names are tried first, so "Northern Ireland" reads as the United Kingdom and "New Mexico" as the United States, never as Ireland or Mexico. Names that double as common words or as United States places (Georgia, Jersey, Reading, Nice, Split) are left out; the country they belong to is still read when the text names it.</remarks>
public static partial class PlaceDirectory
{
    private static readonly Dictionary<string, string> CountryByName = BuildIndex(new Dictionary<string, string[]>
    {
        ["AT"] = ["austria", "osterreich"],
        ["BE"] = ["belgium", "belgique", "belgie"],
        ["BG"] = ["bulgaria"],
        ["CH"] = ["switzerland", "schweiz", "suisse", "svizzera"],
        ["CY"] = ["cyprus"],
        ["CZ"] = ["czechia", "czech republic", "czech", "cesko"],
        ["DE"] = ["germany", "deutschland"],
        ["DK"] = ["denmark", "danmark"],
        ["EE"] = ["estonia"],
        ["ES"] = ["spain", "espana"],
        ["FI"] = ["finland", "suomi"],
        ["FR"] = ["france"],
        ["GB"] = ["united kingdom", "uk", "u.k.", "great britain", "britain", "england", "scotland", "wales", "northern ireland"],
        ["GR"] = ["greece"],
        ["HR"] = ["croatia"],
        ["HU"] = ["hungary"],
        ["IE"] = ["ireland"],
        ["IS"] = ["iceland"],
        ["IT"] = ["italy", "italia"],
        ["LI"] = ["liechtenstein"],
        ["LT"] = ["lithuania"],
        ["LU"] = ["luxembourg"],
        ["LV"] = ["latvia"],
        ["MT"] = ["malta"],
        ["NL"] = ["netherlands", "the netherlands", "holland", "nederland"],
        ["NO"] = ["norway", "norge"],
        ["PL"] = ["poland", "polska"],
        ["PT"] = ["portugal"],
        ["RO"] = ["romania"],
        ["SE"] = ["sweden", "sverige"],
        ["SI"] = ["slovenia"],
        ["SK"] = ["slovakia"],
        ["AL"] = ["albania"],
        ["BA"] = ["bosnia and herzegovina", "bosnia"],
        ["MD"] = ["moldova", "moldavia"],
        ["ME"] = ["montenegro"],
        ["MK"] = ["north macedonia", "macedonia"],
        ["RS"] = ["serbia"],
        ["TR"] = ["turkey", "turkiye"],
        ["UA"] = ["ukraine"],
        ["US"] = ["united states", "united states of america", "usa", "u.s.a.", "u.s.a", "u.s.", "us"],
        ["CA"] = ["canada"],
        ["MX"] = ["mexico"],
        ["BR"] = ["brazil", "brasil"],
        ["AR"] = ["argentina"],
        ["CL"] = ["chile"],
        ["CO"] = ["colombia"],
        ["PE"] = ["peru"],
        ["UY"] = ["uruguay"],
        ["CR"] = ["costa rica"],
        ["AU"] = ["australia"],
        ["NZ"] = ["new zealand"],
        ["SG"] = ["singapore"],
        ["IN"] = ["india"],
        ["JP"] = ["japan"],
        ["KR"] = ["south korea", "korea"],
        ["CN"] = ["china"],
        ["HK"] = ["hong kong"],
        ["TW"] = ["taiwan"],
        ["PH"] = ["philippines"],
        ["ID"] = ["indonesia"],
        ["VN"] = ["vietnam", "viet nam"],
        ["TH"] = ["thailand"],
        ["MY"] = ["malaysia"],
        ["PK"] = ["pakistan"],
        ["BD"] = ["bangladesh"],
        ["LK"] = ["sri lanka"],
        ["IL"] = ["israel"],
        ["AE"] = ["united arab emirates", "uae"],
        ["SA"] = ["saudi arabia"],
        ["QA"] = ["qatar"],
        ["BH"] = ["bahrain"],
        ["KW"] = ["kuwait"],
        ["OM"] = ["oman"],
        ["JO"] = ["jordan"],
        ["EG"] = ["egypt"],
        ["MA"] = ["morocco"],
        ["TN"] = ["tunisia"],
        ["NG"] = ["nigeria"],
        ["KE"] = ["kenya"],
        ["ZA"] = ["south africa"]
    });

    private static readonly Dictionary<string, string> CountryByCity = BuildIndex(new Dictionary<string, string[]>
    {
        ["AT"] = ["vienna", "wien", "graz", "linz", "salzburg", "innsbruck"],
        ["BE"] = ["brussels", "bruxelles", "antwerp", "antwerpen", "ghent", "gent", "leuven", "liege"],
        ["BG"] = ["sofia", "plovdiv", "varna"],
        ["CH"] = ["zurich", "geneva", "geneve", "basel", "lausanne", "bern", "zug", "lugano"],
        ["CY"] = ["nicosia", "limassol"],
        ["CZ"] = ["prague", "praha", "brno", "ostrava"],
        ["DE"] = ["berlin", "munich", "munchen", "hamburg", "frankfurt", "cologne", "koln", "stuttgart", "dusseldorf", "leipzig", "dresden", "nuremberg", "nurnberg", "karlsruhe", "hanover", "hannover", "bonn"],
        ["DK"] = ["copenhagen", "kobenhavn", "aarhus", "odense"],
        ["EE"] = ["tallinn", "tartu"],
        ["ES"] = ["madrid", "barcelona", "valencia", "malaga", "seville", "sevilla", "bilbao", "zaragoza"],
        ["FI"] = ["helsinki", "espoo", "tampere", "oulu"],
        ["FR"] = ["paris", "lyon", "toulouse", "nantes", "bordeaux", "lille", "marseille", "grenoble", "montpellier", "rennes", "sophia antipolis"],
        ["GB"] = ["london", "manchester", "edinburgh", "glasgow", "leeds", "bristol", "birmingham", "cambridge", "oxford", "belfast", "cardiff", "newcastle", "liverpool", "sheffield", "nottingham"],
        ["GR"] = ["athens", "thessaloniki"],
        ["HR"] = ["zagreb"],
        ["HU"] = ["budapest", "debrecen"],
        ["IE"] = ["dublin", "cork", "galway", "limerick"],
        ["IS"] = ["reykjavik"],
        ["IT"] = ["milan", "milano", "rome", "roma", "turin", "torino", "bologna", "florence", "firenze", "naples", "napoli"],
        ["LT"] = ["vilnius", "kaunas"],
        ["LV"] = ["riga"],
        ["MT"] = ["valletta", "sliema"],
        ["NL"] = ["amsterdam", "rotterdam", "utrecht", "the hague", "den haag", "eindhoven", "delft", "groningen", "leiden"],
        ["NO"] = ["oslo", "bergen", "trondheim", "stavanger"],
        ["PL"] = ["warsaw", "warszawa", "krakow", "wroclaw", "gdansk", "poznan", "lodz", "katowice"],
        ["PT"] = ["lisbon", "lisboa", "porto", "braga"],
        ["RO"] = ["bucharest", "bucuresti", "cluj", "cluj napoca", "timisoara", "brasov", "sibiu", "craiova", "oradea", "constanta"],
        ["RS"] = ["belgrade", "novi sad"],
        ["SE"] = ["stockholm", "gothenburg", "goteborg", "malmo", "uppsala", "lund"],
        ["SI"] = ["ljubljana"],
        ["SK"] = ["bratislava", "kosice"],
        ["MD"] = ["chisinau"],
        ["TR"] = ["istanbul", "ankara", "izmir"],
        ["UA"] = ["kyiv", "kiev", "lviv", "kharkiv", "odesa", "odessa", "dnipro"],
        ["US"] = ["new york", "nyc", "san francisco", "bay area", "los angeles", "seattle", "austin", "boston", "chicago", "denver", "atlanta", "miami", "dallas", "houston", "portland", "philadelphia", "phoenix", "san diego", "san jose", "new jersey", "new mexico", "washington dc"],
        ["CA"] = ["toronto", "vancouver", "montreal", "ottawa", "calgary", "waterloo"],
        ["AU"] = ["sydney", "melbourne", "brisbane", "perth", "adelaide", "canberra", "new south wales", "queensland"],
        ["NZ"] = ["auckland", "wellington", "christchurch"],
        ["IN"] = ["bangalore", "bengaluru", "hyderabad", "pune", "mumbai", "delhi", "new delhi", "gurgaon", "gurugram", "noida", "chennai"],
        ["IL"] = ["tel aviv", "haifa", "jerusalem"],
        ["AE"] = ["dubai", "abu dhabi"],
        ["SA"] = ["riyadh", "jeddah"],
        ["QA"] = ["doha"],
        ["JP"] = ["tokyo", "osaka"],
        ["BR"] = ["sao paulo", "rio de janeiro"],
        ["AR"] = ["buenos aires"],
        ["MX"] = ["mexico city", "guadalajara"],
        ["KE"] = ["nairobi"],
        ["NG"] = ["lagos"],
        ["ZA"] = ["cape town", "johannesburg"]
    });

    /// <summary>The member states of the European Union, the countries of the European Economic Area outside it, and Switzerland: the area where a citizen of any of them may live and work without a visa.</summary>
    private static readonly HashSet<string> FreeMovementArea = new(StringComparer.OrdinalIgnoreCase)
    {
        "AT", "BE", "BG", "CY", "CZ", "DE", "DK", "EE", "ES", "FI", "FR", "GR", "HR", "HU", "IE", "IT", "LT", "LU", "LV", "MT", "NL", "PL", "PT", "RO", "SE", "SI", "SK",
        "IS", "LI", "NO", "CH"
    };

    private static readonly Regex KnownName = BuildPattern(CountryByName.Keys.Concat(CountryByCity.Keys));

    /// <summary>Reads one line of the relocation places: a known country, "City, Country" with a known country, or else the line as a name to match as written; null for a blank line.</summary>
    public static RelocationPlace? ReadEntry(string? entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return null;
        }

        string trimmed = entry.Trim();
        int comma = trimmed.LastIndexOf(',');

        if (comma < 0)
        {
            return CountryOf(trimmed) is string countryIso ? new RelocationPlace(countryIso, null) : new RelocationPlace(CityCountryOf(trimmed), trimmed);
        }

        string city = trimmed[..comma].Trim();
        string country = trimmed[(comma + 1)..].Trim();

        return new RelocationPlace(CountryOf(country) ?? CityCountryOf(city), city.Length > 0 ? city : country);
    }

    /// <summary>The ISO code of a country written by its name, one of its aliases or its two-letter code; null when the directory does not know it.</summary>
    public static string? CountryOf(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        string key = Normalize(name);

        if (CountryByName.TryGetValue(key, out string? countryIso))
        {
            return countryIso;
        }

        string upper = name.Trim().ToUpperInvariant();

        return upper.Length == 2 && CountryByName.ContainsValue(upper) ? upper : null;
    }

    /// <summary>The ISO codes of every country a place text names, directly or through one of the cities the directory knows.</summary>
    public static IReadOnlySet<string> CountriesNamedIn(string? placeText)
    {
        HashSet<string> countries = new(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(placeText))
        {
            return countries;
        }

        foreach (Match match in KnownName.Matches(Normalize(placeText)))
        {
            countries.Add(CountryByName.TryGetValue(match.Value, out string? countryIso) ? countryIso : CountryByCity[match.Value]);
        }

        return countries;
    }

    /// <summary>True when the place text names the place as a whole word, in any case and with or without diacritics.</summary>
    public static bool NamesPlace(string? placeText, string? placeName)
    {
        if (string.IsNullOrWhiteSpace(placeText) || string.IsNullOrWhiteSpace(placeName))
        {
            return false;
        }

        string pattern = $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(Normalize(placeName))}(?![\p{{L}}\p{{N}}])";

        return Regex.IsMatch(Normalize(placeText), pattern, RegexOptions.CultureInvariant);
    }

    /// <summary>True when a citizen of the home country may live and work in the country without a visa: anywhere in the European Union, the European Economic Area and Switzerland for a home country inside that area, otherwise only in the home country itself.</summary>
    public static bool HasFreeMovement(string? homeCountryIso, string countryIso)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(countryIso);

        if (string.Equals(homeCountryIso, countryIso, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return homeCountryIso is not null && FreeMovementArea.Contains(homeCountryIso) && FreeMovementArea.Contains(countryIso);
    }

    private static string? CityCountryOf(string city)
    {
        return CountryByCity.TryGetValue(Normalize(city), out string? countryIso) ? countryIso : null;
    }

    /// <summary>Lower case, without diacritics, with hyphens and runs of white space read as one space, so "Cluj-Napoca", "cluj napoca" and "Kraków" match their entries.</summary>
    private static string Normalize(string text)
    {
        string decomposed = text.Normalize(NormalizationForm.FormD);
        StringBuilder bare = new(decomposed.Length);

        foreach (char character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                bare.Append(character is '-' or '–' or '—' ? ' ' : char.ToLowerInvariant(character));
            }
        }

        return Spaced().Replace(bare.ToString().Normalize(NormalizationForm.FormC), " ").Trim();
    }

    private static Dictionary<string, string> BuildIndex(Dictionary<string, string[]> namesByCountry)
    {
        Dictionary<string, string> index = new(StringComparer.Ordinal);

        foreach ((string countryIso, string[] names) in namesByCountry)
        {
            foreach (string name in names)
            {
                index.Add(name, countryIso);
            }
        }

        return index;
    }

    /// <summary>One pattern for every known name, longest first; the text it runs on is normalized, so a single space stands between words.</summary>
    private static Regex BuildPattern(IEnumerable<string> names)
    {
        IEnumerable<string> alternatives = names.Distinct().OrderByDescending(name => name.Length).Select(Regex.Escape);

        return new Regex($@"(?<![\p{{L}}\p{{N}}])({string.Join('|', alternatives)})(?![\p{{L}}\p{{N}}])", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaced();
}
