using JobHunter.Applications;
using JobHunter.Domain;
using JobHunter.Jobs;
using JobHunter.Pipeline;
using JobHunter.Prefill;
using JobHunter.Refresh;
using JobHunter.Settings;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace JobHunter.Components.Pages;

/// <summary>One job: the posting facts, the score card, the application kit with a copy button per section, and the actions that move it forward.</summary>
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
    private IJSRuntime JavaScript { get; set; } = null!;

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        isConfirmingUnpursue = false;
        await LoadAsync();
    }

    private async Task PursueAsync()
    {
        busy = true;

        try
        {
            await Triage.PursueAsync(Id);
            await LoadAsync();
            message = "Pursued. Write the kit when you want it.";
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
            message = result.Refusal ?? "Unpursued; the job is back in the inbox.";
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

    /// <summary>The application status in words; the opening status reads as the pursuit that created it.</summary>
    private static string StatusLabel(ApplicationStatus status)
    {
        return status switch
        {
            ApplicationStatus.Saved => "Pursued",
            ApplicationStatus.Interview1 => "Interview 1",
            ApplicationStatus.Interview2 => "Interview 2",
            _ => status.ToString()
        };
    }

    /// <summary>Names what an unpursue deletes: the application and whichever of its kit, notes, contact, next action and status history it holds.</summary>
    private static string UnpursueLosses(Application application)
    {
        List<string> losses = ["the application"];

        if (application.Kit is not null)
        {
            losses.Add("its kit");
        }

        if (application.Notes.Count > 0)
        {
            losses.Add(application.Notes.Count == 1 ? "its note" : $"its {application.Notes.Count} notes");
        }

        if (application.Contact is not null)
        {
            losses.Add("its contact");
        }

        if (application.NextAction is not null)
        {
            losses.Add("its next action");
        }

        if (application.History.Count > 0)
        {
            losses.Add("its status history");
        }

        return losses.Count == 1 ? losses[0] : $"{string.Join(", ", losses[..^1])} and {losses[^1]}";
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
