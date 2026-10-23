namespace JobHunter.Pipeline;

/// <summary>The region a candidate works from, read off the home country: its working hours are theirs.</summary>
public enum CandidateRegion
{
    None,
    Europe,
    UnitedStates
}

/// <summary>Who the deterministic rules judge for, read once per run from the settings: home country and city, relocation places, accepted posting languages, stack keywords, the compiled title rules and the excluded companies.</summary>
public sealed class CandidateProfile
{
    /// <summary>The language a posting that names none is taken to be written in, and the one accepted when the settings list none.</summary>
    public const string DefaultLanguage = "en";

    private readonly HashSet<string> excludedCompanyNames;

    /// <summary>Builds the profile and compiles its title and stack patterns once.</summary>
    public CandidateProfile(
        string? homeCountryIso,
        string? homeCity,
        IEnumerable<string> relocationPlaces,
        IEnumerable<string> acceptedLanguages,
        IEnumerable<string> stackKeywords,
        IEnumerable<string> titleIncludeTerms,
        IEnumerable<string> titleExcludeTerms,
        IEnumerable<string>? excludedCompanies = null)
    {
        ArgumentNullException.ThrowIfNull(acceptedLanguages);

        Location = new CandidateLocation(homeCountryIso, homeCity, relocationPlaces);
        AcceptedLanguages = ReadLanguages(acceptedLanguages);
        StackKeywords = new StackKeywordMatcher(stackKeywords);
        TitleRules = new TitleRules(titleIncludeTerms, titleExcludeTerms);
        excludedCompanyNames = [.. (excludedCompanies ?? []).Select(DuplicateMatcher.NormalizeCompany).Where(name => name.Length > 0)];
    }

    /// <summary>True when the candidate excluded the company, matched the way deduplication matches company names, so "Acme" also excludes "Acme Inc.".</summary>
    public bool ExcludesCompany(string? company)
    {
        return !string.IsNullOrWhiteSpace(company) && excludedCompanyNames.Contains(DuplicateMatcher.NormalizeCompany(company));
    }

    /// <summary>Where the candidate lives and would move to.</summary>
    public CandidateLocation Location { get; }

    /// <summary>ISO 3166-1 alpha-2 code of the candidate's country, upper case; null when not set.</summary>
    public string? HomeCountryIso => Location.HomeCountryIso;

    /// <summary>The city the candidate lives in, trimmed; empty when not set.</summary>
    public string HomeCity => Location.HomeCity;

    /// <summary>The region the candidate works from: the region of the home country when it lies in Europe or the United States, otherwise none.</summary>
    public CandidateRegion HomeRegion => Location.HomeRegion;

    /// <summary>ISO 639-1 codes, lower case, of the posting languages accepted.</summary>
    public IReadOnlySet<string> AcceptedLanguages { get; }

    /// <summary>The stack keywords that raise the stack-match flag.</summary>
    public StackKeywordMatcher StackKeywords { get; }

    /// <summary>The title include and exclude rules, compiled once.</summary>
    public ITitleRules TitleRules { get; }

    /// <summary>Reads the profile off the settings row.</summary>
    /// <remarks>The settings type is written qualified because the JobHunter.Settings namespace shadows the plain name.</remarks>
    public static CandidateProfile FromSettings(Domain.Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new CandidateProfile(
            settings.HomeCountryIso,
            settings.HomeCity,
            LiteralTermPattern.ReadTerms(settings.RelocationPlaces, '\n'),
            LiteralTermPattern.ReadTerms(settings.AcceptedLanguages, ','),
            LiteralTermPattern.ReadTerms(settings.StackKeywords, ','),
            LiteralTermPattern.ReadTerms(settings.TitleIncludeTerms, '\n'),
            LiteralTermPattern.ReadTerms(settings.TitleExcludeTerms, '\n'),
            LiteralTermPattern.ReadTerms(settings.ExcludedCompanies, '\n'));
    }

    /// <summary>The accepted posting languages of a comma-separated list, as lower-case codes; an empty list accepts the default language only.</summary>
    public static IReadOnlySet<string> ReadLanguages(string commaList)
    {
        ArgumentNullException.ThrowIfNull(commaList);

        return ReadLanguages(LiteralTermPattern.ReadTerms(commaList, ','));
    }

    /// <summary>True when a posting in this language is accepted: the primary subtag of the code (en of en-US) must be in the list, and a posting that names no language counts as English.</summary>
    public bool AcceptsPostingLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return AcceptedLanguages.Contains(DefaultLanguage);
        }

        string code = language.Trim().Split('-', '_')[0];

        return AcceptedLanguages.Contains(code);
    }

    private static IReadOnlySet<string> ReadLanguages(IEnumerable<string> codes)
    {
        HashSet<string> languages = new(LiteralTermPattern.ReadTerms(codes).Select(code => code.ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);

        return languages.Count == 0 ? new HashSet<string>([DefaultLanguage], StringComparer.OrdinalIgnoreCase) : languages;
    }
}
