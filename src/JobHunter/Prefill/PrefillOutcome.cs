using JobHunter.Domain;

namespace JobHunter.Prefill;

/// <summary>How far prefill got with one posting.</summary>
public enum PrefillResult
{
    Filled,
    OpenedUrlOnly,
    Failed
}

/// <summary>What prefill did with one posting: which fields it typed, which ones its selectors did not reach, and the browser window it left open.</summary>
public sealed record PrefillOutcome(PrefillResult Result, AtsKind? Ats, string Url, IReadOnlyList<PrefillField> Filled, IReadOnlyList<PrefillField> Missing, string? Error)
{
    /// <summary>The screening questions the form asked and prefill answered.</summary>
    public IReadOnlyList<ScreeningQuestion> Answered { get; init; } = [];

    /// <summary>The screening questions the form asked that prefill has no answer for, or whose control refused the answer.</summary>
    public IReadOnlyList<ScreeningQuestion> Unanswered { get; init; } = [];

    /// <summary>Records a form that was recognized and typed into.</summary>
    public static PrefillOutcome Fields(AtsKind ats, string url, IReadOnlyList<PrefillField> filled, IReadOnlyList<PrefillField> missing)
    {
        return new PrefillOutcome(PrefillResult.Filled, ats, url, filled, missing, null);
    }

    /// <summary>Records a host no map covers: the posting is open in the browser and nothing was typed.</summary>
    public static PrefillOutcome OpenedUrlOnly(string url)
    {
        return new PrefillOutcome(PrefillResult.OpenedUrlOnly, null, url, [], [], null);
    }

    /// <summary>Records a browser or form failure; a failure before the page opened leaves no window, so the description claims none.</summary>
    public static PrefillOutcome Failed(string url, string error)
    {
        return new PrefillOutcome(PrefillResult.Failed, null, url, [], [], error);
    }

    /// <summary>One line for the job page, always stating that nothing was submitted.</summary>
    public string Describe()
    {
        return Result switch
        {
            PrefillResult.Filled => $"{Ats} form open and filled: {Names(Filled)}.{MissingSentence()}{QuestionsSentence()} Nothing was submitted.",
            PrefillResult.OpenedUrlOnly => "No form is mapped for this host, so the posting is open in the browser and nothing was typed.",
            _ => $"Prefill failed: {Error}"
        };
    }

    /// <summary>The field name as a reader expects to see it on a form.</summary>
    public static string Label(PrefillField field)
    {
        return field switch
        {
            PrefillField.FirstName => "first name",
            PrefillField.LastName => "last name",
            PrefillField.FullName => "full name",
            PrefillField.Email => "email",
            PrefillField.Phone => "phone",
            PrefillField.Location => "location",
            PrefillField.LinkedIn => "LinkedIn",
            PrefillField.Resume => "resume",
            _ => "cover letter"
        };
    }

    private string MissingSentence()
    {
        return Missing.Count == 0 ? string.Empty : $" Not found on this form: {Names(Missing)}.";
    }

    private string QuestionsSentence()
    {
        string answered = Answered.Count == 0 ? string.Empty : $" Answered: {string.Join(", ", Answered.Select(ScreeningQuestions.Label))}.";
        string unanswered = Unanswered.Count == 0 ? string.Empty : $" Left for you: {string.Join(", ", Unanswered.Select(ScreeningQuestions.Label))}.";

        return answered + unanswered;
    }

    private static string Names(IReadOnlyList<PrefillField> fields)
    {
        return fields.Count == 0 ? "nothing" : string.Join(", ", fields.Select(Label));
    }
}
