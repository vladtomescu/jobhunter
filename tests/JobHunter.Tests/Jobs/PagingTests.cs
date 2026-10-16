using JobHunter.Jobs;

namespace JobHunter.Tests.Jobs;

/// <summary>Proves the page arithmetic the lists share: a total fills whole pages rounded up, an empty list still has a first page and a page index never leaves the pages that exist.</summary>
public sealed class PagingTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(50, 1)]
    [InlineData(51, 2)]
    [InlineData(100, 2)]
    [InlineData(101, 3)]
    public void PageCount_ForATotal_RoundsUpAndNeverDropsBelowOne(int totalCount, int expected)
    {
        Assert.Equal(expected, Paging.PageCount(totalCount));
    }

    [Fact]
    public void PageCount_ForAnotherPageSize_FillsPagesOfThatSize()
    {
        Assert.Equal(4, Paging.PageCount(31, 10));
    }

    [Theory]
    [InlineData(-3, 120, 0)]
    [InlineData(1, 120, 1)]
    [InlineData(2, 120, 2)]
    [InlineData(3, 120, 2)]
    [InlineData(5, 0, 0)]
    public void ClampPageIndex_ForAPageOutsideTheTotal_FallsBackToTheNearestExistingPage(int pageIndex, int totalCount, int expected)
    {
        Assert.Equal(expected, Paging.ClampPageIndex(pageIndex, totalCount));
    }
}
