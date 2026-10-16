using JobHunter.Jobs;

namespace JobHunter.Tests.Jobs;

/// <summary>Proves the order the full list is read in follows the grid's sort, and that a reader who changes it starts again at the first page.</summary>
public sealed class JobListFilterTests
{
    [Theory]
    [InlineData(JobListColumn.Company, false)]
    [InlineData(JobListColumn.Title, true)]
    [InlineData(JobListColumn.Age, false)]
    public void SortBy_ADifferentOrder_ReturnsToTheFirstPageAndKeepsTheOrder(JobListColumn column, bool descending)
    {
        JobListFilter filter = new() { PageIndex = 3 };

        filter.SortBy(column, descending);

        Assert.Equal(0, filter.PageIndex);
        Assert.Equal(column, filter.OrderBy);
        Assert.Equal(descending, filter.Descending);
    }

    [Fact]
    public void SortBy_TheOrderAlreadyInForce_KeepsThePage()
    {
        JobListFilter filter = new() { PageIndex = 3 };
        filter.SortBy(JobListColumn.Company, descending: false);
        filter.PageIndex = 2;

        filter.SortBy(JobListColumn.Company, descending: false);

        Assert.Equal(2, filter.PageIndex);
    }

    [Fact]
    public void SortBy_TheNewestFirstThatTheListOpensOn_KeepsThePage()
    {
        JobListFilter filter = new() { PageIndex = 1 };

        filter.SortBy(JobListColumn.Age, descending: true);

        Assert.Equal(1, filter.PageIndex);
    }
}
