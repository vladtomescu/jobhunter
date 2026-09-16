using JobHunter.Domain;

namespace JobHunter.Prefill;

/// <summary>Ashby boards (`jobs.ashbyhq.com`), whose form renders on a dedicated `/application` page after the page script runs.</summary>
/// <remarks>Only the system fields carry readable identifiers; every board-defined question gets a generated one, so those fields are reached through the wrapper that holds their label.</remarks>
public static class AshbyFormMap
{
    private const string FieldEntry = ".ashby-application-form-field-entry";

    /// <summary>The Ashby selector map.</summary>
    public static AtsFormMap Map { get; } = new(
        AtsKind.Ashby,
        "#_systemfield_name",
        new Dictionary<PrefillField, IReadOnlyList<string>>
        {
            [PrefillField.FullName] = ["#_systemfield_name", "input[name=\"_systemfield_name\"]"],
            [PrefillField.Email] = ["#_systemfield_email", "input[name=\"_systemfield_email\"]"],
            [PrefillField.Phone] = ["input[type=\"tel\"]", $"{FieldEntry}:has-text(\"Phone\") input"],
            [PrefillField.Location] = [$"{FieldEntry}:has-text(\"Location\") input"],
            [PrefillField.LinkedIn] = [$"{FieldEntry}:has-text(\"LinkedIn\") input"],
            [PrefillField.Resume] = ["#_systemfield_resume", $"{FieldEntry}:has-text(\"Resume\") input[type=\"file\"]"],
            [PrefillField.CoverLetter] = [$"{FieldEntry}:has-text(\"Cover Letter\") textarea"]
        });
}
