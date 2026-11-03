namespace JobHunter.Prefill;

/// <summary>A screening or self-identification question that prefill recognizes on a form by its label.</summary>
public enum ScreeningQuestion
{
    Salary,
    AvailableFrom,
    UnitedStatesSponsorship,
    HowHeard,
    YearsOfExperience,
    RelativesAtCompany,
    Gender,
    HispanicOrLatino,
    VeteranStatus
}

/// <summary>Recognizes a question by the words of its label, and names it the way a reader expects.</summary>
public static class ScreeningQuestions
{
    /// <summary>The question a form label asks, or null when it is none prefill answers.</summary>
    public static ScreeningQuestion? Recognize(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        string text = label.ToLowerInvariant();

        return text switch
        {
            _ when text.Contains("salary") || text.Contains("compensation expectation") || text.Contains("gehaltsvorstellung") => ScreeningQuestion.Salary,
            _ when text.Contains("available from") || text.Contains("verfügbar ab") => ScreeningQuestion.AvailableFrom,
            _ when text.Contains("sponsorship") && (text.Contains("united states") || text.Contains("u.s.")) => ScreeningQuestion.UnitedStatesSponsorship,
            _ when text.Contains("how did you") && (text.Contains("hear") || text.Contains("learn") || text.Contains("find")) => ScreeningQuestion.HowHeard,
            _ when text.Contains("years of experience") => ScreeningQuestion.YearsOfExperience,
            _ when text.Contains("relative") => ScreeningQuestion.RelativesAtCompany,
            _ when text.TrimStart().StartsWith("gender", StringComparison.Ordinal) => ScreeningQuestion.Gender,
            _ when text.Contains("hispanic") => ScreeningQuestion.HispanicOrLatino,
            _ when text.Contains("veteran status") => ScreeningQuestion.VeteranStatus,
            _ => null
        };
    }

    /// <summary>The question as the job page names it.</summary>
    public static string Label(ScreeningQuestion question)
    {
        return question switch
        {
            ScreeningQuestion.Salary => "salary",
            ScreeningQuestion.AvailableFrom => "available from",
            ScreeningQuestion.UnitedStatesSponsorship => "US sponsorship",
            ScreeningQuestion.HowHeard => "how you heard",
            ScreeningQuestion.YearsOfExperience => "years of experience",
            ScreeningQuestion.RelativesAtCompany => "relatives at the company",
            ScreeningQuestion.Gender => "gender",
            ScreeningQuestion.HispanicOrLatino => "Hispanic or Latino",
            _ => "veteran status"
        };
    }
}
