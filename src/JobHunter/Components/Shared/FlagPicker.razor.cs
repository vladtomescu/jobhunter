using JobHunter.Domain;
using JobHunter.Jobs;
using Microsoft.AspNetCore.Components;

namespace JobHunter.Components.Shared;

/// <summary>The flag filter of a list: a drop-down of checkboxes, one per flag, whose ticked flags a job must all carry.</summary>
public sealed partial class FlagPicker
{
    /// <summary>The ticked flags; none means the list is not narrowed by flag.</summary>
    [Parameter]
    [EditorRequired]
    public IReadOnlyCollection<JobFlag> Flags { get; set; } = [];

    /// <summary>Raised with the flags that are ticked after the reader ticks or unticks one.</summary>
    [Parameter]
    public EventCallback<IReadOnlyCollection<JobFlag>> FlagsChanged { get; set; }

    /// <summary>The candidate's settings, which word the flags that name a city, a keyword or a pay threshold.</summary>
    [Parameter]
    [EditorRequired]
    public Domain.Settings Settings { get; set; } = null!;

    private string Summary => Flags.Count == 0
        ? "Flags: any"
        : $"Flags: {string.Join(", ", Enum.GetValues<JobFlag>().Where(Flags.Contains).Select(flag => JobDisplay.FlagCode(flag, Settings)))}";

    private async Task ToggleAsync(JobFlag flag, bool isTicked)
    {
        IReadOnlyCollection<JobFlag> flags = [.. Enum.GetValues<JobFlag>().Where(value => value == flag ? isTicked : Flags.Contains(value))];

        await FlagsChanged.InvokeAsync(flags);
    }
}
