namespace JobHunter.Sources.WeWorkRemotely;

/// <summary>The three We Work Remotely category feeds this source reads.</summary>
public static class WeWorkRemotelyFeeds
{
    /// <summary>The category feed URLs; one request per feed per run.</summary>
    public static IReadOnlyList<string> CategoryUrls { get; } =
    [
        "https://weworkremotely.com/categories/remote-back-end-programming-jobs.rss",
        "https://weworkremotely.com/categories/remote-full-stack-programming-jobs.rss",
        "https://weworkremotely.com/categories/remote-devops-sysadmin-jobs.rss"
    ];
}
