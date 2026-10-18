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
    /// <summary>The rows of the Skills card, one per importance in the score schema's words, with the label each row shows.</summary>
    private static readonly (string Importance, string Label)[] SkillRows = [("primary", "Primary"), ("required", "Required"), ("nice_to_have", "Nice to have")];

    private JobDetailView? view;
    private SkillCatalog skillCatalog = new([]);
    private Domain.Settings settings = Domain.Settings.CreateDefault();
    private string? matchedStackKeyword;
    private ApplicationStatus chosenStatus = ApplicationStatus.Applied;
    private ApplicationChannel channel = ApplicationChannel.Ats;
    private string? statusNote;
    private string? message;
    private bool busy;
    private bool isConfirmingUnpursue;
    private bool isConfirmingDelete;
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

    [Inject]
    private JobRetentionService Retention { get; set; } = null!;

    [Inject]
    private NavigationManager Navigation { get; set; } = null!;

    /// <summary>Why the cover-letter button is off when no key is detected; nothing while it is on.</summary>
    private string? CoverLetterButtonTitle => KeyDetector.IsPresent ? null : $"No API key is detected: run /jh:cover {Id} in Claude Code instead.";

    /// <summary>The statuses the Status card offers: all of them for a tracked job, all but Saved for one with no application, because Saved is the Save button's job.</summary>
    private IEnumerable<ApplicationStatus> StatusChoices => Enum.GetValues<ApplicationStatus>().Where(status => view?.Application is not null || status != ApplicationStatus.Saved);

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        isConfirmingUnpursue = false;
        isConfirmingDelete = false;
        coverLetterError = null;
        await LoadAsync();
        ResetStatusForm();
    }

    private async Task PursueAsync()
    {
        busy = true;

        try
        {
            await Triage.PursueAsync(Id);
            await LoadAsync();
            ResetStatusForm();
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
            ResetStatusForm();
            message = result.Refusal ?? "Unsaved; the job is back in the inbox.";
        }
        finally
        {
            busy = false;
        }
    }

    private void AskToConfirmDelete()
    {
        message = null;
        isConfirmingDelete = true;
    }

    private void CancelDelete()
    {
        isConfirmingDelete = false;
    }

    /// <summary>Deletes the job and opens the full list; a refusal stays on the page with its reason.</summary>
    private async Task DeleteAsync()
    {
        busy = true;

        try
        {
            JobRetentionOutcome outcome = await Retention.DeleteJobAsync(Id);
            isConfirmingDelete = false;

            if (outcome.IsRefused)
            {
                message = outcome.Refusal;
                await LoadAsync();

                return;
            }

            await Backlog.RecountAsync();
        }
        finally
        {
            busy = false;
        }

        Navigation.NavigateTo("/jobs");
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

    private async Task ChangeStatusAsync()
    {
        busy = true;

        try
        {
            ApplicationStatus status = chosenStatus;
            string? note = string.IsNullOrWhiteSpace(statusNote) ? null : statusNote;

            await ApplicationChanges.ChangeJobStatusAsync(Id, status, channel, note);
            await LoadAsync();
            ResetStatusForm();
            message = $"Status set to {JobDisplay.StatusLabel(status)}.";
        }
        finally
        {
            busy = false;
        }
    }

    /// <summary>Points the Status card at what is stored: the application's status and channel, or Applied and Ats when the job has no application yet.</summary>
    private void ResetStatusForm()
    {
        chosenStatus = view?.Application?.Status ?? ApplicationStatus.Applied;
        channel = view?.Application?.Channel ?? ApplicationChannel.Ats;
        statusNote = null;
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

    private static string StepClass(ProgressStep step)
    {
        if (step.Status is ApplicationStatus.Rejected or ApplicationStatus.Withdrawn or ApplicationStatus.Ghosted)
        {
            return "closed current";
        }

        string reached = step.ReachedAt is null && !step.IsCurrent ? "ahead" : "reached";
        return step.IsCurrent ? $"{reached} current" : reached;
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

    /// <summary>What a skill chip's colour means: the skill's CV mark and who set it, or that it is not judged yet.</summary>
    private static string SkillMeaning(Skill? skill)
    {
        string mark = skill?.CvMark switch
        {
            CvMark.Yes => "on the CV",
            CvMark.Close => "close to the CV",
            CvMark.No => "not on the CV",
            _ => "not judged against the CV yet"
        };

        return skill?.CvMarkSetBy is CvMarkSetBy setBy ? $"{mark}, marked by the {setBy.ToString().ToLowerInvariant()}" : mark;
    }

    private async Task LoadAsync()
    {
        view = await JobQueries.GetJobAsync(Id);
        skillCatalog = await JobQueries.GetSkillCatalogAsync();
        settings = await SettingsReader.GetAsync();
        matchedStackKeyword = view is null
            ? null
            : CandidateProfile.FromSettings(settings).StackKeywords.FindKeyword(view.Job.Title, view.Job.Tags, view.Job.DescriptionText);
    }
}
