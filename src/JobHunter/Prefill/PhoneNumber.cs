using System.Text.RegularExpressions;

namespace JobHunter.Prefill;

/// <summary>Splits a phone number written in international form into its country code and the rest, for a form whose country-code picker carries the code.</summary>
public static partial class PhoneNumber
{
    /// <summary>The code and the national number of <c>+44 7700 900123</c> style input, or null when the number does not start with a code set apart by a space or a dash.</summary>
    public static (string Code, string National)? Split(string? phone)
    {
        Match match = InternationalForm().Match(phone?.Trim() ?? string.Empty);

        return match.Success ? (match.Groups["code"].Value, match.Groups["national"].Value.Trim()) : null;
    }

    [GeneratedRegex(@"^(?<code>\+\d{1,3})[\s-]+(?<national>\d[\d\s-]*)$")]
    private static partial Regex InternationalForm();
}
