namespace JobHunter.Prefill;

/// <summary>What prefill types into a form: the contact details from settings, the resume file to upload and the cover note when the job already has a kit.</summary>
public sealed record PrefillValues(string FirstName, string LastName, string Email, string Phone, string Location, string LinkedInUrl, string ResumePdfPath, string? CoverLetter)
{
    /// <summary>Reads the values from the settings row, with the cover note supplied by the caller because it belongs to the application kit, not to settings.</summary>
    public static PrefillValues From(Domain.Settings settings, string? coverLetter)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new PrefillValues(
            settings.FirstName,
            settings.LastName,
            settings.Email,
            settings.Phone,
            settings.Location,
            settings.LinkedInUrl,
            settings.ResumePdfPath,
            coverLetter);
    }

    /// <summary>The text for one field, or null when nothing is recorded for it and the form control is therefore left alone.</summary>
    public string? ValueFor(PrefillField field)
    {
        string? value = field switch
        {
            PrefillField.FirstName => FirstName,
            PrefillField.LastName => LastName,
            PrefillField.FullName => $"{FirstName} {LastName}".Trim(),
            PrefillField.Email => Email,
            PrefillField.Phone => Phone,
            PrefillField.Location => Location,
            PrefillField.LinkedIn => LinkedInUrl,
            PrefillField.Resume => ResumePdfPath,
            PrefillField.CoverLetter => CoverLetter,
            _ => null
        };

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
