using JobHunter.Domain;
using JobHunter.Prefill;

namespace JobHunter.Tests.Prefill;

public class PrefillOutcomeTests
{
    [Fact]
    public void OpenedUrlOnly_UnsupportedHost_ReportsNothingTypedAndNoSystem()
    {
        PrefillOutcome outcome = PrefillOutcome.OpenedUrlOnly("https://careers.example.com/openings/42");

        Assert.Equal(PrefillResult.OpenedUrlOnly, outcome.Result);
        Assert.Null(outcome.Ats);
        Assert.Empty(outcome.Filled);
        Assert.Empty(outcome.Missing);
        Assert.Null(outcome.Error);
        Assert.Contains("nothing was typed", outcome.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_FieldsFilled_NamesThemAndSaysNothingWasSubmitted()
    {
        PrefillOutcome outcome = PrefillOutcome.Fields(
            AtsKind.Greenhouse,
            "https://job-boards.greenhouse.io/everlaw/jobs/4705236006",
            [PrefillField.Resume, PrefillField.FirstName, PrefillField.Email],
            [PrefillField.Location]);

        string description = outcome.Describe();

        Assert.Contains("Greenhouse", description, StringComparison.Ordinal);
        Assert.Contains("resume, first name, email", description, StringComparison.Ordinal);
        Assert.Contains("Not found on this form: location", description, StringComparison.Ordinal);
        Assert.Contains("Nothing was submitted.", description, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_EveryFieldReached_LeavesOutTheNotFoundSentence()
    {
        string description = PrefillOutcome.Fields(AtsKind.Lever, "https://jobs.lever.co/org/id/apply", [PrefillField.FullName], []).Describe();

        Assert.DoesNotContain("Not found", description, StringComparison.Ordinal);
        Assert.Contains("Nothing was submitted.", description, StringComparison.Ordinal);
    }

    [Fact]
    public void Failed_BrowserOrFormProblem_KeepsTheReasonAndSaysTheWindowIsOpen()
    {
        PrefillOutcome outcome = PrefillOutcome.Failed("https://jobs.ashbyhq.com/org/id/application", "the browser is not installed.");

        Assert.Equal(PrefillResult.Failed, outcome.Result);
        Assert.Contains("the browser is not installed.", outcome.Describe(), StringComparison.Ordinal);
        Assert.Contains("window is open", outcome.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void Label_EveryField_HasAReadableName()
    {
        foreach (PrefillField field in Enum.GetValues<PrefillField>())
        {
            Assert.False(string.IsNullOrWhiteSpace(PrefillOutcome.Label(field)));
        }
    }
}
