namespace JobHunter.Pipeline;

/// <summary>The region a candidate works from: its working hours are theirs, and an onsite role inside it needs no move.</summary>
public enum CandidateRegion
{
    None,
    Europe,
    UnitedStates
}

/// <summary>Who the deterministic rules judge for, read once per run from the settings: home country and city, accepted remote regions and posting languages, stack keywords and the compiled title rules.</summary>
public sealed class CandidateProfile
{
    /// <summary>The language a posting that names none is taken to be written in, and the one accepted when the settings list none.</summary>
    public const string DefaultLanguage = "en";

    /// <summary>Builds the profile and compiles its title and stack patterns once.</summary>
    public CandidateProfile(
        string? homeCountryIso,
        string? homeCity,
        bool acceptsEuropeRemote,
        bool acceptsUnitedStatesRemote,
        IEnumerable<string> acceptedLanguages,
        IEnumerable<string> stackKeywords,
        IEnumerable<string> titleIncludeTerms,
        IEnumerable<string> titleExcludeTerms)
    {
        ArgumentNullException.ThrowIfNull(acceptedLanguages);

        HomeCountryIso = string.IsNullOrWhiteSpace(homeCountryIso) ? null : homeCountryIso.Trim().ToUpperInvariant();
        HomeCity = homeCity?.Trim() ?? string.Empty;
        AcceptsEuropeRemote = acceptsEuropeRemote;
        AcceptsUnitedStatesRemote = acceptsUnitedStatesRemote;
        HomeRegion = ReadHomeRegion(HomeCountryIso, acceptsEuropeRemote, acceptsUnitedStatesRemote);
        AcceptedLanguages = ReadLanguages(acceptedLanguages);
        StackKeywords = new StackKeywordMatcher(stackKeywords);
        TitleRules = new TitleRules(titleIncludeTerms, titleExcludeTerms);
    }

    /// <summary>ISO 3166-1 alpha-2 code of the candidate's country, upper case; null when not set.</summary>
    public string? HomeCountryIso { get; }

    /// <summary>The city the candidate lives in, trimmed; empty when not set.</summary>
    public string HomeCity { get; }

    /// <summary>True when remote roles restricted to Europe are accepted.</summary>
    public bool AcceptsEuropeRemote { get; }

    /// <summary>True when remote roles restricted to the United States are accepted.</summary>
    public bool AcceptsUnitedStatesRemote { get; }

    /// <summary>The region the candidate works from: the region of the home country when it lies in Europe or the United States, otherwise the first accepted remote region, Europe before the United States.</summary>
    public CandidateRegion HomeRegion { get; }

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
            settings.AcceptEuropeRemote,
            settings.AcceptUnitedStatesRemote,
            LiteralTermPattern.ReadTerms(settings.AcceptedLanguages, ','),
            LiteralTermPattern.ReadTerms(settings.StackKeywords, ','),
            LiteralTermPattern.ReadTerms(settings.TitleIncludeTerms, '\n'),
            LiteralTermPattern.ReadTerms(settings.TitleExcludeTerms, '\n'));
    }

    /// <summary>The accepted posting languages of a comma-separated list, as lower-case codes; an empty list accepts the default language only.</summary>
    public static IReadOnlySet<string> ReadLanguages(string commaList)
    {
        ArgumentNullException.ThrowIfNull(commaList);

        return ReadLanguages(LiteralTermPattern.ReadTerms(commaList, ','));
    }

    /// <summary>True when remote roles restricted to the region are accepted.</summary>
    public bool AcceptsRemoteIn(CandidateRegion region)
    {
        return region switch
        {
            CandidateRegion.Europe => AcceptsEuropeRemote,
            CandidateRegion.UnitedStates => AcceptsUnitedStatesRemote,
            _ => false
        };
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

    private static CandidateRegion ReadHomeRegion(string? homeCountryIso, bool acceptsEuropeRemote, bool acceptsUnitedStatesRemote)
    {
        if (GeographyRules.IsEuropeanCountry(homeCountryIso))
        {
            return CandidateRegion.Europe;
        }

        if (GeographyRules.IsUnitedStatesCountry(homeCountryIso))
        {
            return CandidateRegion.UnitedStates;
        }

        if (acceptsEuropeRemote)
        {
            return CandidateRegion.Europe;
        }

        return acceptsUnitedStatesRemote ? CandidateRegion.UnitedStates : CandidateRegion.None;
    }
}
