namespace JobHunter.Jobs;

/// <summary>The page arithmetic every paged list shares: how many rows a page carries and which pages a total fills.</summary>
public static class Paging
{
    /// <summary>How many rows one page of a list carries.</summary>
    public const int PageSize = 50;

    /// <summary>How many pages the rows fill; an empty list still has one, so that the first page always exists.</summary>
    public static int PageCount(int totalCount, int pageSize = PageSize)
    {
        return Math.Max(1, (totalCount + pageSize - 1) / pageSize);
    }

    /// <summary>The page index pulled into the pages that exist, so that a page emptied by a skip or an import falls back to the last one.</summary>
    public static int ClampPageIndex(int pageIndex, int totalCount)
    {
        return Math.Clamp(pageIndex, 0, PageCount(totalCount) - 1);
    }
}
