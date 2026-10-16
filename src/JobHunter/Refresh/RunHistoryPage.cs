namespace JobHunter.Refresh;

/// <summary>One page of the run history: its runs, the zero-based page they are, and the number of runs stored in all.</summary>
public sealed record RunHistoryPage(IReadOnlyList<RefreshRunSummary> Runs, int PageIndex, int TotalCount);
