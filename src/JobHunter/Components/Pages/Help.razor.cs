using JobHunter.Domain;
using JobHunter.Llm;
using JobHunter.Settings;
using Microsoft.AspNetCore.Components;

namespace JobHunter.Components.Pages;

/// <summary>The help page: what the profile files and the Claude Code skills do, with which copy of each profile file is in use, and how a job gets its score, its class and its flags, worded from the candidate's own settings where a value helps.</summary>
public partial class Help
{
    private Domain.Settings settings = Domain.Settings.CreateDefault();

    [Inject]
    private SettingsService SettingsService { get; set; } = null!;

    [Inject]
    private PromptCatalog Prompts { get; set; } = null!;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        settings = await SettingsService.GetAsync();
    }

    /// <summary>What reads a profile file, in the words of the buttons that do it.</summary>
    private static string ReadBy(string fileName)
    {
        return fileName switch
        {
            PromptCatalog.ProfileFileName => "Scoring, kits and cover letters",
            PromptCatalog.RubricFileName => "Scoring",
            PromptCatalog.QuestionsFileName => "Kits",
            PromptCatalog.CoverLetterFileName => "Cover letters",
            _ => "Open and prefill"
        };
    }
}
