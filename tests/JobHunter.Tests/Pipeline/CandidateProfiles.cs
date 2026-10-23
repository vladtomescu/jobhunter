using JobHunter.Domain;
using JobHunter.Pipeline;

namespace JobHunter.Tests.Pipeline;

/// <summary>The candidate profiles the rule tables are written against, each built through the settings row the way a refresh builds it.</summary>
public static class CandidateProfiles
{
    /// <summary>A candidate living in Utrecht, in the Netherlands, who would move to Germany, Lisbon, London or Australia: English and Dutch postings, Java and Kotlin, the default title terms.</summary>
    public static CandidateProfile NetherlandsJava { get; } = Build("NL", "Utrecht", ["Germany", "Lisbon, Portugal", "London, United Kingdom", "Australia"], "en,nl", "Java, Kotlin");

    /// <summary>A second candidate: Berlin, Germany, who would move anywhere in Germany or to Utrecht, English postings only, Java, the default title terms.</summary>
    public static CandidateProfile Berlin { get; } = Build("DE", "Berlin", ["Germany", "Utrecht, Netherlands"], "en", "Java");

    /// <summary>A candidate in Austin, in the United States, who would move to Denver or to Canada.</summary>
    public static CandidateProfile Austin { get; } = Build("US", "Austin", ["Denver, United States", "Canada"], "en", "Go");

    /// <summary>A candidate who set no home country, home city or relocation place, the way a fresh install starts: the geography rules keep every place.</summary>
    public static CandidateProfile WithoutPlaces { get; } = Build(null, string.Empty, [], "en", "Java, Kotlin");

    private static CandidateProfile Build(string? homeCountryIso, string homeCity, string[] relocationPlaces, string acceptedLanguages, string stackKeywords)
    {
        JobHunter.Domain.Settings settings = JobHunter.Domain.Settings.CreateDefault();
        settings.ConfigureCandidate(
            homeCountryIso,
            homeCity,
            acceptedLanguages,
            "EUR",
            stackKeywords,
            ContractPreference.Either,
            hasUnitedStatesWorkAuthorization: false,
            highPayThresholdPerYear: null,
            JobHunter.Domain.Settings.DefaultTitleIncludeTerms,
            JobHunter.Domain.Settings.DefaultTitleExcludeTerms);
        settings.ConfigureRelocationPlaces(string.Join('\n', relocationPlaces));

        return CandidateProfile.FromSettings(settings);
    }
}
