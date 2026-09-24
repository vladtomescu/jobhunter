using JobHunter.Domain;
using JobHunter.Settings;
using Microsoft.AspNetCore.Components;

namespace JobHunter.Components.Pages;

/// <summary>The help page: how a job gets its score, its class and its flags, worded from the candidate's own settings where a value helps.</summary>
public partial class Help
{
    private Domain.Settings settings = Domain.Settings.CreateDefault();

    [Inject]
    private SettingsService SettingsService { get; set; } = null!;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        settings = await SettingsService.GetAsync();
    }
}
