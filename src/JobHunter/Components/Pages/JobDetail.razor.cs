using JobHunter.Applications;
using JobHunter.Domain;
using JobHunter.Jobs;
using JobHunter.Prefill;
using JobHunter.Settings;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace JobHunter.Components.Pages;

/// <summary>One job: the posting facts, the score card, the application kit with a copy button per section, and the actions that move it forward.</summary>
public partial class JobDetail
{
    private JobDetailView? view;
    private ApplicationChannel channel = ApplicationChannel.Ats;
    private string? appliedNote;
    private string? message;
    private bool busy;

    /// <summary>The job this page shows.</summary>
    [Parameter]
    public Guid Id { get; set; }

    [Inject]
    private JobQueryService JobQueries { get; set; } = null!;

    [Inject]
    private TriageService Triage { get; set; } = null!;

    [Inject]
    private ApplicationService ApplicationChanges { get; set; } = null!;

    [Inject]
    private SettingsService SettingsReader { get; set; } = null!;

    [Inject]
    private PrefillService FormPrefill { get; set; } = null!;

    [Inject]
    private IJSRuntime JavaScript { get; set; } = null!;

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        await LoadAsync();
    }

    private async Task PursueAsync()
    {
        busy = true;

        try
        {
            await Triage.PursueAsync(Id);
            await LoadAsync();
            message = "Pursued; the kit is below.";
        }
        finally
        {
            busy = false;
        }
    }

    private async Task SkipAsync()
    {
        busy = true;

        try
        {
            await Triage.SkipAsync(Id);
            await LoadAsync();
            message = "Skipped; the job leaves the inbox.";
        }
        finally
        {
            busy = false;
        }
    }

    private async Task MarkAppliedAsync()
    {
        busy = true;

        try
        {
            JobHunter.Domain.Settings settings = await SettingsReader.GetAsync();
            string? cvVersion = string.IsNullOrWhiteSpace(settings.ResumePdfPath) ? null : Path.GetFileName(settings.ResumePdfPath);

            await ApplicationChanges.MarkAppliedAsync(Id, channel, cvVersion, appliedNote);
            appliedNote = null;
            await LoadAsync();
            message = "Marked applied.";
        }
        finally
        {
            busy = false;
        }
    }

    private async Task PrefillAsync()
    {
        if (view is null)
        {
            return;
        }

        busy = true;
        message = "Opening the form in the browser.";

        try
        {
            PrefillOutcome outcome = await FormPrefill.RunAsync(view.Job.ApplyUrl ?? view.Job.PostingUrl, view.Application?.Kit?.CoverNote);
            message = outcome.Describe();
        }
        finally
        {
            busy = false;
        }
    }

    private async Task CopyAsync(string text)
    {
        await JavaScript.InvokeVoidAsync("copyToClipboard", text);
        message = "Copied to the clipboard.";
    }

    private static string FitSummaryText(ApplicationKit kit)
    {
        return string.Join(Environment.NewLine, kit.FitSummary);
    }

    private static string CallQuestionsText(ApplicationKit kit)
    {
        return string.Join(Environment.NewLine, kit.CallQuestions);
    }

    private static string AtsAnswersText(ApplicationKit kit)
    {
        return string.Join(Environment.NewLine + Environment.NewLine, kit.AtsAnswers.Select(answer => $"{answer.Question}{Environment.NewLine}{answer.Answer}"));
    }

    private static string SourceKinds(Job job)
    {
        return job.Sources.Count == 0 ? "none recorded" : string.Join(", ", job.Sources.Select(reference => reference.Kind).Distinct());
    }

    private static string Tell(bool? value)
    {
        return value switch
        {
            true => "yes",
            false => "no",
            _ => "unknown"
        };
    }

    private async Task LoadAsync()
    {
        view = await JobQueries.GetJobAsync(Id);
    }
}
