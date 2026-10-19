using JobHunter.Domain;

namespace JobHunter.Prefill;

/// <summary>Greenhouse boards (`job-boards.greenhouse.io`, its European twin and the older `boards.greenhouse.io`), whose application form sits on the posting page itself.</summary>
/// <remarks>Name, email and phone carry stable identifiers; the link and cover-letter fields are board-defined questions, reached by their label instead.</remarks>
public static class GreenhouseFormMap
{
    /// <summary>The frame a company career site embeds its Greenhouse form in; its source is the form on a Greenhouse host, which <see cref="Map"/> covers.</summary>
    public const string EmbedFrameSelector = "iframe#grnhse_iframe";

    /// <summary>True for a company career page that embeds a Greenhouse form, which Greenhouse marks with the <c>gh_jid</c> query parameter.</summary>
    public static bool IsEmbedPage(string? url)
    {
        return Uri.TryCreate(url?.Trim(), UriKind.Absolute, out Uri? parsed)
            && parsed.Query.TrimStart('?').Split('&').Any(pair => pair.StartsWith("gh_jid=", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The Greenhouse selector map.</summary>
    public static AtsFormMap Map { get; } = new(
        AtsKind.Greenhouse,
        "#first_name",
        new Dictionary<PrefillField, IReadOnlyList<string>>
        {
            [PrefillField.FirstName] = ["#first_name", "input[autocomplete=\"given-name\"]"],
            [PrefillField.LastName] = ["#last_name", "input[autocomplete=\"family-name\"]"],
            [PrefillField.Email] = ["#email", "input[autocomplete=\"email\"]"],
            [PrefillField.Phone] = ["#phone", "input[type=\"tel\"]"],
            [PrefillField.Location] = ["#job_application_location", "input[autocomplete=\"address-level2\"]", ".field-wrapper:has-text(\"Location\") input[type=\"text\"]"],
            [PrefillField.LinkedIn] = ["input[aria-label*=\"LinkedIn\"]", ".field-wrapper:has-text(\"LinkedIn\") input[type=\"text\"]"],
            [PrefillField.Resume] = ["#resume", "input[type=\"file\"][name=\"resume\"]"],
            [PrefillField.CoverLetter] = ["#cover_letter_text", "textarea[aria-label*=\"Cover\"]"]
        })
    {
        PhoneCountryPickerSelector = "input#country[role=\"combobox\"]"
    };
}
