using Microsoft.AspNetCore.Components;

namespace JobHunter.Components.Shared;

/// <summary>The thin bar across the top of the window that runs while the page loads.</summary>
public sealed partial class PageLoader : IDisposable
{
    [Inject]
    private PageLoading Loading { get; set; } = null!;

    /// <summary>Stops following the loading state once the layout goes away.</summary>
    public void Dispose()
    {
        Loading.Changed -= StateHasChanged;
    }

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        Loading.Changed += StateHasChanged;
    }
}
