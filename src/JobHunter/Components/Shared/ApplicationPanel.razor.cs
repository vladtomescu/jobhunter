using System.Globalization;
using JobHunter.Applications;
using JobHunter.Domain;
using Microsoft.AspNetCore.Components;

namespace JobHunter.Components.Shared;

/// <summary>The application panel the pipeline's detail row and the job page share: one activity timeline of notes and status changes, the next action, the contact, the comp discussed and the CV sent.</summary>
/// <remarks>The editors are plain inputs over the panel's own fields, for the reason the pipeline gives: a form under an edit context that the reload after a save replaces tears the circuit down. A save tells the parent through <see cref="Changed"/>, and the parent's reload hands a new application in, which resets the fields.</remarks>
public sealed partial class ApplicationPanel
{
    private const string DueDateFormat = "yyyy-MM-dd";

    /// <summary>How many characters of an entry show before Show more.</summary>
    private const int LongEntryLength = 300;

    private Application? shownApplication;
    private string newNote = string.Empty;
    private string nextAction = string.Empty;
    private string nextActionDueText = string.Empty;
    private string contactName = string.Empty;
    private string contactRole = string.Empty;
    private string contactLink = string.Empty;
    private string compDiscussed = string.Empty;
    private bool isEditingContact;
    private bool isEditingComp;
    private readonly HashSet<string> openEntryKeys = [];

    /// <summary>The application the panel shows and edits.</summary>
    [Parameter]
    [EditorRequired]
    public Application Application { get; set; } = null!;

    /// <summary>Raised after a save, so the parent reloads the application.</summary>
    [Parameter]
    public EventCallback Changed { get; set; }

    [Inject]
    private ApplicationService ApplicationService { get; set; } = null!;

    /// <summary>The notes and the status changes as one list, newest first.</summary>
    private List<TimelineEntry> Timeline { get; set; } = [];

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (ReferenceEquals(shownApplication, Application))
        {
            return;
        }

        shownApplication = Application;
        newNote = string.Empty;
        nextAction = Application.NextAction ?? string.Empty;
        nextActionDueText = Application.NextActionDue?.ToString(DueDateFormat, CultureInfo.InvariantCulture) ?? string.Empty;
        compDiscussed = Application.CompDiscussed ?? string.Empty;
        isEditingContact = false;
        isEditingComp = false;
        Timeline = BuildTimeline(Application);
    }

    private static List<TimelineEntry> BuildTimeline(Application application)
    {
        IEnumerable<TimelineEntry> statusChanges = application.History.Select((entry, index) => new TimelineEntry($"status-{index}", entry.At, entry.Status, entry.Note));
        IEnumerable<TimelineEntry> notes = application.Notes.Select((note, index) => new TimelineEntry($"note-{index}", note.At, null, note.Text));

        return [.. statusChanges.Concat(notes).OrderByDescending(entry => entry.At)];
    }

    private void ToggleEntry(string key)
    {
        if (!openEntryKeys.Add(key))
        {
            openEntryKeys.Remove(key);
        }
    }

    private void BeginContactEdit()
    {
        contactName = Application.Contact?.Name ?? string.Empty;
        contactRole = Application.Contact?.Role ?? string.Empty;
        contactLink = Application.Contact?.Link ?? string.Empty;
        isEditingContact = true;
    }

    private void BeginCompEdit()
    {
        compDiscussed = Application.CompDiscussed ?? string.Empty;
        isEditingComp = true;
    }

    private async Task AddNoteAsync()
    {
        if (string.IsNullOrWhiteSpace(newNote))
        {
            return;
        }

        await ApplicationService.AddNoteAsync(Application.Id, newNote);
        await Changed.InvokeAsync();
    }

    private async Task SaveNextActionAsync()
    {
        DateOnly? due = DateOnly.TryParseExact(nextActionDueText.Trim(), DueDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly parsed) ? parsed : null;

        await ApplicationService.SetNextActionAsync(Application.Id, nextAction, due);
        await Changed.InvokeAsync();
    }

    private async Task SaveContactAsync()
    {
        await ApplicationService.SetContactAsync(Application.Id, contactName, contactRole, contactLink);
        await Changed.InvokeAsync();
    }

    private async Task SaveCompDiscussedAsync()
    {
        await ApplicationService.SetCompDiscussedAsync(Application.Id, compDiscussed);
        await Changed.InvokeAsync();
    }

    /// <summary>One line of the timeline: a status change when <paramref name="Status"/> is set, a note otherwise.</summary>
    private sealed record TimelineEntry(string Key, DateTimeOffset At, ApplicationStatus? Status, string? Text);
}
