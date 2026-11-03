using JobHunter.Domain;
using JobHunter.Pipeline;

namespace JobHunter.Prefill;

/// <summary>The selectors one applicant tracking system uses for the standard applicant fields, plus the selector that proves its form has rendered.</summary>
/// <param name="Ats">The system this map describes.</param>
/// <param name="ReadySelector">A selector that exists once the application form is on the page; prefill waits for it before it types anything.</param>
/// <param name="Selectors">The candidate selectors per field, tried in order, so that a board that spells a field differently still resolves.</param>
public sealed record AtsFormMap(AtsKind Ats, string ReadySelector, IReadOnlyDictionary<PrefillField, IReadOnlyList<string>> Selectors)
{
    /// <summary>The country-code picker next to the phone field, when the system has one; the phone is then typed without its country code, which the picker carries.</summary>
    public string? PhoneCountryPickerSelector { get; init; }

    /// <summary>Turns a posting link into the link of its application form, when the system puts the form on a page of its own that the posting only links to.</summary>
    public Func<string, string>? FormUrl { get; init; }

    /// <summary>Every system prefill can type into; a posting on any other host is opened and left alone.</summary>
    public static IReadOnlyList<AtsFormMap> All { get; } = [GreenhouseFormMap.Map, LeverFormMap.Map, AshbyFormMap.Map, PersonioFormMap.Map];

    /// <summary>Finds the map for an apply URL by its host, which is what decides the form, not the system recorded on the job.</summary>
    public static AtsFormMap? ForUrl(string? applyUrl)
    {
        AtsKind? ats = AtsKindParser.FromUrl(applyUrl);

        return ats is null ? null : All.FirstOrDefault(map => map.Ats == ats);
    }

    /// <summary>The selectors that reach one field, empty when this system has no such field.</summary>
    public IReadOnlyList<string> SelectorsFor(PrefillField field)
    {
        return Selectors.TryGetValue(field, out IReadOnlyList<string>? selectors) ? selectors : [];
    }
}
