using System.Security.Cryptography;
using System.Text;

namespace JobHunter.Pipeline;

/// <summary>The identity of a posting and of its text: short hexadecimal digests used to recognize a job across runs and to notice a rewritten description.</summary>
public static class JobFingerprint
{
    /// <summary>The fingerprint of a job: the digest of its canonical apply URL.</summary>
    public static string ForCanonicalUrl(string canonicalApplyUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalApplyUrl);

        return Digest(canonicalApplyUrl);
    }

    /// <summary>The digest of a description, which decides whether a job has to be scored again.</summary>
    public static string ForDescription(string descriptionText)
    {
        ArgumentNullException.ThrowIfNull(descriptionText);

        return Digest(descriptionText);
    }

    private static string Digest(string value)
    {
        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(value));

        return Convert.ToHexStringLower(hash);
    }
}
