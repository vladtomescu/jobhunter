using System.Text.RegularExpressions;
using JobHunter.Domain;

namespace JobHunter.Prefill;

/// <summary>Personio boards (`*.jobs.personio.com` and `*.jobs.personio.de`), whose form lives on a dedicated `/job/{id}/apply` page that the posting only links to.</summary>
/// <remarks>Every standard field carries a readable name; a board-defined field is named <c>custom_attribute_{id}</c> and shows its label as its placeholder, which is how the LinkedIn field is reached. The page also carries a Cloudflare check that prefill leaves alone.</remarks>
public static partial class PersonioFormMap
{
    /// <summary>The Personio selector map.</summary>
    public static AtsFormMap Map { get; } = new(
        AtsKind.Personio,
        "input[name=\"email\"]",
        new Dictionary<PrefillField, IReadOnlyList<string>>
        {
            [PrefillField.FirstName] = ["input[name=\"first_name\"]"],
            [PrefillField.LastName] = ["input[name=\"last_name\"]"],
            [PrefillField.Email] = ["input[name=\"email\"]"],
            [PrefillField.Phone] = ["input[name=\"phone\"]"],
            [PrefillField.LinkedIn] = ["input[name^=\"custom_attribute\"][placeholder*=\"LinkedIn\" i]"],
            [PrefillField.Resume] = ["input[type=\"file\"][name=\"documents.cv\"]", "#doc-input-cv"]
        })
    {
        FormUrl = ToFormUrl
    };

    /// <summary>The apply page of a posting link such as <c>/job/2772681</c>, keeping its query; any other link, the apply page included, comes back as it is.</summary>
    public static string ToFormUrl(string postingUrl)
    {
        if (!Uri.TryCreate(postingUrl.Trim(), UriKind.Absolute, out Uri? parsed) || !PostingPath().IsMatch(parsed.AbsolutePath))
        {
            return postingUrl;
        }

        UriBuilder form = new(parsed) { Path = $"{parsed.AbsolutePath.TrimEnd('/')}/apply" };

        return form.Uri.ToString();
    }

    [GeneratedRegex(@"^/job/\d+/?$")]
    private static partial Regex PostingPath();
}
