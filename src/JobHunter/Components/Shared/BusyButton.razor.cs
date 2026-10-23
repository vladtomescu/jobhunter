using Microsoft.AspNetCore.Components;

namespace JobHunter.Components.Shared;

/// <summary>A button that shows a spinner and turns itself off while the work it started runs.</summary>
public sealed partial class BusyButton
{
    /// <summary>The label, with its icon.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <summary>The work a click starts; the button spins until it ends.</summary>
    [Parameter]
    public EventCallback OnClick { get; set; }

    /// <summary>The page's own busy state, for a submit button whose work the form starts; it replaces the click's.</summary>
    [Parameter]
    public bool? Busy { get; set; }

    /// <summary>Turns the button off without a spinner, while other work on the page runs.</summary>
    [Parameter]
    public bool Disabled { get; set; }

    /// <summary>The button type; "submit" only for the button that sends its form.</summary>
    [Parameter]
    public string Type { get; set; } = "button";

    /// <summary>Any other attribute, such as class or title, which goes on the button as written.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    private bool clickRunning;

    private bool IsBusy => Busy ?? clickRunning;

    private async Task ClickAsync()
    {
        if (IsBusy || Disabled || !OnClick.HasDelegate)
        {
            return;
        }

        // SQLite runs its async calls synchronously, so the work would end before the spinner is drawn without this yield.
        clickRunning = true;
        try
        {
            await Task.Yield();
            await OnClick.InvokeAsync();
        }
        finally
        {
            clickRunning = false;
        }
    }
}
