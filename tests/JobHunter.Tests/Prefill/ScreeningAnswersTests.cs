using JobHunter.Domain;
using JobHunter.Prefill;
using JobHunter.Tests.Applications;

namespace JobHunter.Tests.Prefill;

/// <summary>Covers the screening questions prefill recognizes by label, the answers it gives them, and the phone split for a country-code picker.</summary>
public sealed class ScreeningAnswersTests
{
    private const string AnswersFile = """
        # Prefill answers

        Free text and examples such as `- key: answer` in a sentence are ignored.

        - years of experience: 10
        - how heard: LinkedIn
        - relatives at company: No
        - gender: Male
        - hispanic or latino:
        - veteran status: not a protected veteran
        """;

    [Theory]
    [InlineData("What are your salary requirements?\n*", ScreeningQuestion.Salary)]
    [InlineData("Will you now or in the future require sponsorship to work in the United States?*", ScreeningQuestion.UnitedStatesSponsorship)]
    [InlineData("How did you learn about this role?*", ScreeningQuestion.HowHeard)]
    [InlineData("How did you hear about us?", ScreeningQuestion.HowHeard)]
    [InlineData("How many years of experience do you have in a related field?*", ScreeningQuestion.YearsOfExperience)]
    [InlineData("Do you have any close relatives that are currently employed by Example Co?", ScreeningQuestion.RelativesAtCompany)]
    [InlineData("Gender", ScreeningQuestion.Gender)]
    [InlineData("Are you Hispanic/Latino?", ScreeningQuestion.HispanicOrLatino)]
    [InlineData("Veteran Status", ScreeningQuestion.VeteranStatus)]
    public void Recognize_KnownQuestionLabel_NamesTheQuestion(string label, ScreeningQuestion expected)
    {
        Assert.Equal(expected, ScreeningQuestions.Recognize(label));
    }

    [Theory]
    [InlineData("If applicable, please specify how you heard about this role")]
    [InlineData("Will you require sponsorship to work in the United Kingdom?")]
    [InlineData("LinkedIn Profile")]
    [InlineData("Website")]
    [InlineData("")]
    public void Recognize_OtherLabel_IsNoQuestion(string label)
    {
        Assert.Null(ScreeningQuestions.Recognize(label));
    }

    [Fact]
    public void For_AnswersFile_ReadsTheKeyedLinesAndLeavesEmptyAnswersOut()
    {
        ScreeningAnswers answers = ScreeningAnswers.For(AnswersFile, JobHunter.Domain.Settings.CreateDefault(), NewJob());

        Assert.Equal("10", answers.For(ScreeningQuestion.YearsOfExperience));
        Assert.Equal("LinkedIn", answers.For(ScreeningQuestion.HowHeard));
        Assert.Equal("Male", answers.For(ScreeningQuestion.Gender));
        Assert.Equal("not a protected veteran", answers.For(ScreeningQuestion.VeteranStatus));
        Assert.Null(answers.For(ScreeningQuestion.HispanicOrLatino));
    }

    [Fact]
    public void For_NoUnitedStatesWorkAuthorization_AnswersYesToSponsorship()
    {
        ScreeningAnswers answers = ScreeningAnswers.For(AnswersFile, JobHunter.Domain.Settings.CreateDefault(), NewJob());

        Assert.Equal("Yes", answers.For(ScreeningQuestion.UnitedStatesSponsorship));
    }

    [Fact]
    public void For_PostingWithAPayRange_AnswersSalaryWithThatRangeInItsOwnCurrencyAndPeriod()
    {
        Job job = NewJob();
        job.RecordCompensation(120000m, 150000m, "USD", CompPeriod.Year, 110000m, 138000m);

        ScreeningAnswers answers = ScreeningAnswers.For(AnswersFile, JobHunter.Domain.Settings.CreateDefault(), job);

        Assert.Equal("120,000 - 150,000 USD per year", answers.For(ScreeningQuestion.Salary));
    }

    [Fact]
    public void For_PostingWithoutPay_LeavesSalaryUnanswered()
    {
        Assert.Null(ScreeningAnswers.For(AnswersFile, JobHunter.Domain.Settings.CreateDefault(), NewJob()).For(ScreeningQuestion.Salary));
    }

    [Theory]
    [InlineData("+44 7700 900123", "+44", "7700 900123")]
    [InlineData("+1-415-555-0172", "+1", "415-555-0172")]
    public void Split_InternationalNumber_SeparatesTheCode(string phone, string code, string national)
    {
        Assert.Equal((code, national), PhoneNumber.Split(phone));
    }

    [Theory]
    [InlineData("07700 900123")]
    [InlineData("+447700900123")]
    [InlineData("")]
    public void Split_NumberWithoutASeparatedCode_IsNotSplit(string phone)
    {
        Assert.Null(PhoneNumber.Split(phone));
    }

    [Fact]
    public void Describe_AnsweredAndUnansweredQuestions_NamesBoth()
    {
        PrefillOutcome outcome = PrefillOutcome.Fields(AtsKind.Greenhouse, "https://job-boards.greenhouse.io/org/jobs/1", [PrefillField.Email], [])
            with { Answered = [ScreeningQuestion.Salary, ScreeningQuestion.Gender], Unanswered = [ScreeningQuestion.HispanicOrLatino] };

        string description = outcome.Describe();

        Assert.Contains("Answered: salary, gender.", description, StringComparison.Ordinal);
        Assert.Contains("Left for you: Hispanic or Latino.", description, StringComparison.Ordinal);
        Assert.Contains("Nothing was submitted.", description, StringComparison.Ordinal);
    }

    private static Job NewJob()
    {
        return TestJobs.NewJob("Example Co", [], null, isActive: true, new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.Zero));
    }
}
