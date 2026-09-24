using JobHunter.Domain;
using JobHunter.Pipeline;

namespace JobHunter.Tests.Pipeline;

/// <summary>The candidate profiles the rule tables are written against, each built through the settings row the way a refresh builds it.</summary>
public static class CandidateProfiles
{
    /// <summary>A candidate in the Netherlands: remote in Europe and the United States, English and Dutch postings, Java and Kotlin, the default title terms.</summary>
    public static CandidateProfile NetherlandsJava { get; } = Build("NL", acceptEuropeRemote: true, acceptUnitedStatesRemote: true, "en,nl", "Java, Kotlin");

    /// <summary>The candidate in the Netherlands with United States only remote switched off.</summary>
    public static CandidateProfile NetherlandsJavaWithoutUnitedStatesRemote { get; } = Build("NL", acceptEuropeRemote: true, acceptUnitedStatesRemote: false, "en,nl", "Java, Kotlin");

    /// <summary>A second candidate: Germany, remote in Europe only, English postings only, Java, the default title terms.</summary>
    public static CandidateProfile Berlin { get; } = Build("DE", acceptEuropeRemote: true, acceptUnitedStatesRemote: false, "en", "Java");

    /// <summary>A candidate in the United States who accepts United States remote only.</summary>
    public static CandidateProfile UnitedStatesOnly { get; } = Build("US", acceptEuropeRemote: false, acceptUnitedStatesRemote: true, "en", "Go");

    private static CandidateProfile Build(string homeCountryIso, bool acceptEuropeRemote, bool acceptUnitedStatesRemote, string acceptedLanguages, string stackKeywords)
    {
        JobHunter.Domain.Settings settings = JobHunter.Domain.Settings.CreateDefault();
        settings.ConfigureCandidate(
            homeCountryIso,
            acceptEuropeRemote,
            acceptUnitedStatesRemote,
            acceptedLanguages,
            "EUR",
            stackKeywords,
            ContractPreference.Either,
            hasUnitedStatesWorkAuthorization: false,
            highPayThresholdPerYear: null,
            JobHunter.Domain.Settings.DefaultTitleIncludeTerms,
            JobHunter.Domain.Settings.DefaultTitleExcludeTerms);

        return CandidateProfile.FromSettings(settings);
    }
}
