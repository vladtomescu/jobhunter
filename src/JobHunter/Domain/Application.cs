namespace JobHunter.Domain;

/// <summary>An application for one job, moving through the pipeline with a history row behind every status change.</summary>
public sealed class Application
{
    private Application()
    {
    }

    public Guid Id { get; private set; }

    public Guid JobId { get; private set; }

    public ApplicationStatus Status { get; private set; }

    public DateTimeOffset StatusChangedAt { get; private set; }

    public DateTimeOffset? AppliedAt { get; private set; }

    public ApplicationChannel? Channel { get; private set; }

    public string? CvVersion { get; private set; }

    public List<ApplicationHistoryEntry> History { get; private set; } = [];

    public List<ApplicationNote> Notes { get; private set; } = [];

    public ApplicationContact? Contact { get; private set; }

    public string? NextAction { get; private set; }

    public DateOnly? NextActionDue { get; private set; }

    public string? CompDiscussed { get; private set; }

    public KitState KitState { get; private set; }

    public string? KitError { get; private set; }

    public ApplicationKit? Kit { get; private set; }

    /// <summary>Creates an application at its opening status and writes the first history row.</summary>
    public static Application Create(Guid jobId, ApplicationStatus status, DateTimeOffset at, string? note)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(jobId, Guid.Empty);

        Application application = new()
        {
            Id = Guid.CreateVersion7(),
            JobId = jobId,
            Status = status,
            StatusChangedAt = at,
            KitState = KitState.None
        };
        application.History.Add(new ApplicationHistoryEntry(status, at, note));

        if (status == ApplicationStatus.Applied)
        {
            application.AppliedAt = at;
        }

        return application;
    }

    /// <summary>Moves the application to another status; any transition is allowed and each one appends a history row.</summary>
    public void MoveTo(ApplicationStatus status, DateTimeOffset at, string? note)
    {
        Status = status;
        StatusChangedAt = at;
        History.Add(new ApplicationHistoryEntry(status, at, note));

        if (status == ApplicationStatus.Applied && AppliedAt is null)
        {
            AppliedAt = at;
        }
    }

    /// <summary>Records that the application was submitted, with the channel and the resume version that was sent.</summary>
    public void MarkApplied(DateTimeOffset at, ApplicationChannel channel, string? cvVersion, string? note)
    {
        Channel = channel;
        CvVersion = cvVersion;
        AppliedAt = at;
        MoveTo(ApplicationStatus.Applied, at, note);
    }

    /// <summary>Appends a note to the timeline.</summary>
    public void AddNote(string text, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        Notes.Add(new ApplicationNote(at, text));
    }

    /// <summary>Records the contact person, or clears it when no name is given.</summary>
    public void RecordContact(string? name, string? role, string? link)
    {
        Contact = string.IsNullOrWhiteSpace(name) ? null : new ApplicationContact(name, role, link);
    }

    /// <summary>Records what happens next and when it is due.</summary>
    public void PlanNextAction(string? action, DateOnly? due)
    {
        NextAction = string.IsNullOrWhiteSpace(action) ? null : action;
        NextActionDue = NextAction is null ? null : due;
    }

    /// <summary>Records what was discussed about compensation, in whatever words the conversation used.</summary>
    public void RecordCompDiscussed(string? compDiscussed)
    {
        CompDiscussed = string.IsNullOrWhiteSpace(compDiscussed) ? null : compDiscussed;
    }

    /// <summary>Marks the kit as being written.</summary>
    public void BeginKit()
    {
        KitState = KitState.Generating;
        KitError = null;
    }

    /// <summary>Stores a finished kit; lint issues travel with the kit and do not block it.</summary>
    public void AttachKit(ApplicationKit kit)
    {
        ArgumentNullException.ThrowIfNull(kit);

        Kit = kit;
        KitState = KitState.Ready;
        KitError = null;
    }

    /// <summary>Marks kit generation as failed with the reason.</summary>
    public void FailKit(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        KitState = KitState.Failed;
        KitError = error;
    }
}
