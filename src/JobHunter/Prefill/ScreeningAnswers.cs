using System.Globalization;
using JobHunter.Domain;

namespace JobHunter.Prefill;

/// <summary>The answer prefill gives each screening question: the posting's own pay range for salary, else the file's salary, the work authorization in settings for United States sponsorship, and the prefill answers file for the rest.</summary>
public sealed class ScreeningAnswers
{
    private static readonly IReadOnlyDictionary<string, ScreeningQuestion> FileKeys = new Dictionary<string, ScreeningQuestion>(StringComparer.OrdinalIgnoreCase)
    {
        ["salary"] = ScreeningQuestion.Salary,
        ["available from"] = ScreeningQuestion.AvailableFrom,
        ["years of experience"] = ScreeningQuestion.YearsOfExperience,
        ["how heard"] = ScreeningQuestion.HowHeard,
        ["relatives at company"] = ScreeningQuestion.RelativesAtCompany,
        ["gender"] = ScreeningQuestion.Gender,
        ["hispanic or latino"] = ScreeningQuestion.HispanicOrLatino,
        ["veteran status"] = ScreeningQuestion.VeteranStatus
    };

    private readonly Dictionary<ScreeningQuestion, string> answers = [];

    /// <summary>Builds the answers for one posting.</summary>
    /// <param name="answersFile">The prefill answers file: <c>- key: answer</c> lines, any other line ignored.</param>
    /// <param name="settings">The settings, whose work authorization answers the United States sponsorship question.</param>
    /// <param name="job">The posting, whose pay range answers the salary question when it states one, ahead of the file's salary.</param>
    public static ScreeningAnswers For(string answersFile, Domain.Settings settings, Job job)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(job);

        ScreeningAnswers result = new();

        foreach (string line in (answersFile ?? string.Empty).Split('\n'))
        {
            string entry = line.Trim();
            int colon = entry.IndexOf(':', StringComparison.Ordinal);

            if (!entry.StartsWith("- ", StringComparison.Ordinal) || colon < 0)
            {
                continue;
            }

            string key = entry[2..colon].Trim();
            string answer = entry[(colon + 1)..].Trim();

            if (FileKeys.TryGetValue(key, out ScreeningQuestion question) && answer.Length > 0)
            {
                result.answers[question] = answer;
            }
        }

        result.answers[ScreeningQuestion.UnitedStatesSponsorship] = settings.HasUnitedStatesWorkAuthorization ? "No" : "Yes";

        if (SalaryRange(job) is string salary)
        {
            result.answers[ScreeningQuestion.Salary] = salary;
        }

        return result;
    }

    /// <summary>The answer to one question, or null when there is none and the question is left for the reader.</summary>
    public string? For(ScreeningQuestion question)
    {
        return answers.TryGetValue(question, out string? answer) ? answer : null;
    }

    /// <summary>The posting's pay as it states it, for example <c>120,000 - 150,000 USD per year</c>; null when it states none.</summary>
    public static string? SalaryRange(Job job)
    {
        ArgumentNullException.ThrowIfNull(job);

        decimal?[] bounds = [job.CompMin, job.CompMax];
        string[] figures = [.. bounds.OfType<decimal>().Distinct().Select(value => value.ToString("#,0", CultureInfo.InvariantCulture))];

        if (figures.Length == 0)
        {
            return null;
        }

        string currency = string.IsNullOrWhiteSpace(job.CompCurrency) ? string.Empty : $" {job.CompCurrency}";
        string period = job.CompPeriod is CompPeriod compPeriod ? $" per {compPeriod.ToString().ToLowerInvariant()}" : string.Empty;

        return $"{string.Join(" - ", figures)}{currency}{period}";
    }
}
