using JobHunter.Pipeline;

namespace JobHunter.Tests.Pipeline;

/// <summary>Proves that the fingerprint identifies a posting the same way on every run, and that a rewritten description is noticed.</summary>
public sealed class JobFingerprintTests
{
    private const string CanonicalUrl = "https://boards.greenhouse.io/acme/jobs/123";

    [Fact]
    public void ForCanonicalUrl_CalledTwice_ReturnsTheSameFingerprint()
    {
        Assert.Equal(JobFingerprint.ForCanonicalUrl(CanonicalUrl), JobFingerprint.ForCanonicalUrl(CanonicalUrl));
    }

    [Fact]
    public void ForCanonicalUrl_ForTheSameJobReachedThroughTrackedLinks_ReturnsOneFingerprint()
    {
        string fromFeed = JobFingerprint.ForCanonicalUrl(UrlCanonicalizer.Canonicalize($"{CanonicalUrl}?gh_src=abc"));
        string fromDataset = JobFingerprint.ForCanonicalUrl(UrlCanonicalizer.Canonicalize($"{CanonicalUrl}/#apply"));

        Assert.Equal(fromFeed, fromDataset);
    }

    [Fact]
    public void ForCanonicalUrl_ForADifferentJob_ReturnsADifferentFingerprint()
    {
        Assert.NotEqual(JobFingerprint.ForCanonicalUrl(CanonicalUrl), JobFingerprint.ForCanonicalUrl($"{CanonicalUrl}4"));
    }

    [Fact]
    public void ForCanonicalUrl_ForAKnownUrl_ReturnsTheDigestOfThatUrl()
    {
        Assert.Equal("b50710f48d27251fed3066517475ae7837520e28", JobFingerprint.ForCanonicalUrl(CanonicalUrl));
    }

    [Fact]
    public void ForDescription_WhenTheTextChanges_ReturnsADifferentHash()
    {
        Assert.NotEqual(JobFingerprint.ForDescription("We run .NET on Kubernetes."), JobFingerprint.ForDescription("We run .NET on Kubernetes. Updated."));
    }

    [Fact]
    public void ForDescription_ForTheSameText_ReturnsTheSameHash()
    {
        Assert.Equal(JobFingerprint.ForDescription("We run .NET on Kubernetes."), JobFingerprint.ForDescription("We run .NET on Kubernetes."));
    }
}
