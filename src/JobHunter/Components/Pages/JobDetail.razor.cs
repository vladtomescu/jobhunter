using JobHunter.Applications;
using JobHunter.Domain;
using JobHunter.Jobs;
using JobHunter.Llm;
using JobHunter.Pipeline;
using JobHunter.Prefill;
using JobHunter.Refresh;
using JobHunter.Settings;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace JobHunter.Components.Pages;

/// <summary>One job: the posting facts, the score card, the application kit with a copy button per section, the cover letter as it will read, and the actions that move it forward.</summary>
public partial class JobDetail
{
    private JobDetailView? view;
    private Domain.Settings settings = Domain.Settings.CreateDefault();
    private string? matchedStackKeyword;
    private ApplicationChannel channel = ApplicationChannel.Ats;
    private string? appliedNote;
    private string? message;
    private bool busy;
    private bool isConfirmingUnpursue;
    private bool isWritingCoverLetter;
    private string? coverLetterError;

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
    private JobRescoreService Rescorer { get; set; } = null!;

    [Inject]
    private ScoreBacklog Backlog { get; set; } = null!;

    [Inject]
    private CoverLetterService CoverLetters { get; set; } = null!;

    [Inject]
    private ApiKeyDetector KeyDetector { get; set; } = null!;

    [Inject]
    private PromptCatalog Prompts { get; set; } = null!;

    [Inject]
    private IJSRuntime JavaScript { get; set; } = null!;

    /// <summary>Why the cover-letter button is off when no key is detected; nothing while it is on.</summary>
    private string? CoverLetterButtonTitle => KeyDetector.IsPresent ? null : $"No API key is detected: run /jh:cover {Id} in Claude Code instead.";

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        isConfirmingUnpursue = false;
        coverLetterError = null;
        await LoadAsync();
    }

    private async Task PursueAsync()
    {
        busy = true;

        try
        {
            await Triage.PursueAsync(Id);
            await LoadAsync();
            message = "Saved. Write the kit when you want it.";
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

    private void AskToConfirmUnpursue()
    {
        message = null;
        isConfirmingUnpursue = true;
    }

    private void CancelUnpursue()
    {
        isConfirmingUnpursue = false;
    }

    private async Task UnpursueAsync()
    {
        busy = true;

        try
        {
            UnpursueResult result = await Triage.UnpursueAsync(Id);
            isConfirmingUnpursue = false;
            await LoadAsync();
            message = result.Refusal ?? "Unsaved; the job is back in the inbox.";
        }
        finally
        {
            busy = false;
        }
    }

    private async Task WriteKitAsync()
    {
        busy = true;
        message = "Writing the kit.";

        try
        {
            Application application = await Triage.WriteKitAsync(Id);
            await LoadAsync();
            message = application.KitState switch
            {
                KitState.Ready => "Kit written again; see below.",
                KitState.Failed => $"Kit writing failed: {application.KitError}",
                _ => "Kit writing finished."
            };
        }
        finally
        {
            busy = false;
        }
    }

    private async Task WriteCoverLetterAsync()
    {
        busy = true;
        isWritingCoverLetter = true;
        coverLetterError = null;
        message = "Writing the cover letter.";

        try
        {
            CoverLetterStoreResult result = await CoverLetters.WriteAsync(Id);
            await LoadAsync();
            coverLetterError = result.Refusal;
            message = result switch
            {
                { IsStored: false } => $"Cover letter writing failed: {result.Refusal}",
                { LintIssues.Count: > 0 } => "Cover letter written; the lint found issues, listed with it.",
                _ => "Cover letter written; see below."
            };
        }
        finally
        {
            busy = false;
            isWritingCoverLetter = false;
        }
    }

    private CoverLetterText ComposeCoverLetter(ApplicationCoverLetter coverLetter)
    {
        return CoverLetterText.Compose(coverLetter, Prompts.CoverLetterHeaderLines, settings);
    }

    private async Task MarkAppliedAsync()
    {
        busy = true;

        try
        {
            JobHunter.Domain.Settings currentSettings = await SettingsReader.GetAsync();
            string? cvVersion = string.IsNullOrWhiteSpace(currentSettings.ResumePdfPath) ? null : Path.GetFileName(currentSettings.ResumePdfPath);

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

    private async Task ScoreAgainAsync()
    {
        busy = true;
        message = "Scoring this job.";

        try
        {
            RescoreResult result = await Rescorer.RescoreAsync(Id);
            await Backlog.RecountAsync();
            await LoadAsync();
            message = result.Describe();
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

    private static string LinkUrl(Job job)
    {
        return job.ApplyUrl ?? job.PostingUrl;
    }

    /// <summary>The application status in words, as the pipeline names it.</summary>
    private static string StatusLabel(ApplicationStatus status)
    {
        return status switch
        {
            ApplicationStatus.Interview1 => "Interview 1",
            ApplicationStatus.Interview2 => "Interview 2",
            _ => status.ToString()
        };
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
        settings = await SettingsReader.GetAsync();
        matchedStackKeyword = view is null
            ? null
            : CandidateProfile.FromSettings(settings).StackKeywords.FindKeyword(view.Job.Title, view.Job.Tags, view.Job.DescriptionText);
    }
}
