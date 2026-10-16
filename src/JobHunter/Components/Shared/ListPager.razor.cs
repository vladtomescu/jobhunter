using JobHunter.Jobs;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace JobHunter.Components.Shared;

/// <summary>The page bar every paged list shows under its rows: first, previous, the page and the total, next, last.</summary>
public sealed partial class ListPager
{
    /// <summary>The zero-based page being shown.</summary>
    [Parameter]
    [EditorRequired]
    public int PageIndex { get; set; }

    /// <summary>How many rows match across all pages.</summary>
    [Parameter]
    [EditorRequired]
    public int TotalCount { get; set; }

    /// <summary>How many rows one page carries.</summary>
    [Parameter]
    public int PageSize { get; set; } = Paging.PageSize;

    /// <summary>Raised with the zero-based page the reader asked for.</summary>
    [Parameter]
    public EventCallback<int> PageChanged { get; set; }

    [Inject]
    private IJSRuntime Js { get; set; } = null!;

    private int PageCount => Paging.PageCount(TotalCount, PageSize);

    private bool IsFirst => PageIndex <= 0;

    private bool IsLast => PageIndex >= PageCount - 1;

    private async Task GoToAsync(int pageIndex)
    {
        await PageChanged.InvokeAsync(pageIndex);

        // The bar sits under the rows, so the new page is read from its top.
        await Js.InvokeVoidAsync("scrollTo", 0, 0);
    }
}
