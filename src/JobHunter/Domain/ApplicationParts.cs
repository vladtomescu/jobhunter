namespace JobHunter.Domain;

/// <summary>One status change, kept forever as the audit trail of an application.</summary>
public sealed record ApplicationHistoryEntry(ApplicationStatus Status, DateTimeOffset At, string? Note);

/// <summary>A free note written on an application at a point in time.</summary>
public sealed record ApplicationNote(DateTimeOffset At, string Text);

/// <summary>The person on the other side of an application.</summary>
public sealed record ApplicationContact(string Name, string? Role, string? Link);

/// <summary>One standard application question and the answer written for it.</summary>
public sealed record AtsAnswer(string Question, string Answer);

/// <summary>The paste-ready application kit: fit summary, cover note, standard answers, questions for the first call and the resume to attach.</summary>
/// <remarks>The answers are a nested owned collection, which the storage layer cannot bind to a constructor parameter, so they are set through the initializer.</remarks>
public sealed record ApplicationKit(
    List<string> FitSummary,
    string CoverNote,
    List<string> CallQuestions,
    string ResumePath,
    string Language,
    DateTimeOffset GeneratedAt,
    string Model,
    List<string> LintIssues)
{
    /// <summary>The standard application questions with the answers written for them.</summary>
    public List<AtsAnswer> AtsAnswers { get; init; } = [];
}
