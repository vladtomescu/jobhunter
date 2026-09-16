using JobHunter.Domain;

namespace JobHunter.Prefill;

/// <summary>Lever boards (`jobs.lever.co`), whose form lives on a dedicated `/apply` page and names every control through the form field name.</summary>
/// <remarks>Lever asks for one full name rather than a first and last name, and parses the uploaded resume to fill the text fields, which is why the upload happens first.</remarks>
public static class LeverFormMap
{
    /// <summary>The Lever selector map.</summary>
    public static AtsFormMap Map { get; } = new(
        AtsKind.Lever,
        "input[name=\"name\"]",
        new Dictionary<PrefillField, IReadOnlyList<string>>
        {
            [PrefillField.FullName] = ["input[name=\"name\"]"],
            [PrefillField.Email] = ["input[name=\"email\"]"],
            [PrefillField.Phone] = ["input[name=\"phone\"]"],
            [PrefillField.Location] = ["#location-input", "input[name=\"location\"]"],
            [PrefillField.LinkedIn] = ["input[name=\"urls[LinkedIn]\"]", "input[name=\"urls[LinkedIn URL]\"]"],
            [PrefillField.Resume] = ["#resume-upload-input", "input[type=\"file\"][name=\"resume\"]"],
            [PrefillField.CoverLetter] = ["textarea[name=\"comments\"]"]
        });
}
