using JobHunter.Llm;
using JobHunter.Llm.Contracts;

namespace JobHunter.Tests.Llm;

/// <summary>Proves the lint catches every rule the outward voice has to keep, and leaves a clean kit alone.</summary>
public sealed class KitLintTests
{
    [Fact]
    public void Inspect_WithTheSavedCleanKit_ReportsNothing()
    {
        KitPayload kit = LlmJson.Read<KitPayload>(LlmFixtures.Read(LlmFixtures.KitPayloadFile)).Payload!;

        Assert.Empty(KitLint.Inspect(kit));
    }

    [Theory]
    [InlineData("I am looking for 90000 EUR per year.", "currency amount")]
    [InlineData("My rate is 65 EUR per hour.", "currency amount")]
    [InlineData("The range you published, $120,000, works for me.", "currency amount")]
    [InlineData("I can start on 2026-11-01.", "calendar date")]
    [InlineData("I can start on 1.11.2026.", "calendar date")]
    [InlineData("I can start on November 3.", "calendar date")]
    [InlineData("I have a notice period to serve first.", "notice period")]
    [InlineData("I build the agentic harness our tooling runs on.", "agentic")]
    [InlineData("This is exactly the work I want to do!", "exclamation mark")]
    [InlineData("The platform — the whole of it — is mine to run.", "em dashes")]
    [InlineData("I am not just a service author, but a platform owner.", "not just")]
    [InlineData("Write to me at someone@example.com.", "email address")]
    [InlineData("Call me on +1 555 010 2233.", "phone number")]
    [InlineData("My work is at https://example.com/platform.", "web link")]
    public void Inspect_WithACoverNoteThatBreaksARule_ReportsThatRule(string coverNote, string expectedRule)
    {
        IReadOnlyList<string> issues = KitLint.Inspect(NewKit(coverNote: coverNote));

        Assert.Contains(issues, issue => issue.Contains(expectedRule, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Open to discuss. [CONFIRM]")]
    [InlineData("Around 90000 EUR per year would work. [CONFIRM]")]
    [InlineData("I could start on 2026-11-01. [CONFIRM]")]
    [InlineData("I have a notice period to serve. [CONFIRM]")]
    public void Inspect_WithAFigureOrADateBesideTheMarker_ReportsNothing(string answer)
    {
        Assert.Empty(KitLint.Inspect(NewKit(answer: answer)));
    }

    [Fact]
    public void Inspect_WithTheLinkedInPlaceholder_ReportsNothing()
    {
        Assert.Empty(KitLint.Inspect(NewKit(answer: "LinkedIn profile URL [CONFIRM]")));
    }

    [Fact]
    public void Inspect_WithAMarkerOnAnotherLine_StillReportsTheFigure()
    {
        KitPayload kit = NewKit(answer: $"Open to discuss. [CONFIRM]{Environment.NewLine}For reference, my last contract was 90000 EUR per year.");

        Assert.Contains(KitLint.Inspect(kit), issue => issue.Contains("currency amount", StringComparison.Ordinal));
    }

    [Fact]
    public void Inspect_WithAnIssue_NamesTheFieldAndTheOffendingFragment()
    {
        IReadOnlyList<string> issues = KitLint.Inspect(NewKit(coverNote: "This is the role I want!"));

        string issue = Assert.Single(issues);
        Assert.StartsWith("cover_note:", issue, StringComparison.Ordinal);
        Assert.Contains("want!", issue, StringComparison.Ordinal);
    }

    [Fact]
    public void Inspect_WithBrokenRulesInSeveralFields_ReportsOnePerFinding()
    {
        KitPayload kit = NewKit(coverNote: "This is the role I want!", answer: "I will be free after my notice period.");

        Assert.Equal(2, KitLint.Inspect(kit).Count);
    }

    [Fact]
    public void Inspect_WithABrokenRuleInTheFitSummary_ReportsTheBulletThatBrokeIt()
    {
        KitPayload kit = NewKit() with { FitSummary = ["A plain fact.", "I ship the agentic harness."] };

        Assert.Contains(KitLint.Inspect(kit), issue => issue.StartsWith("fit_summary[1]:", StringComparison.Ordinal));
    }

    private static KitPayload NewKit(string? coverNote = null, string? answer = null)
    {
        return new KitPayload(
            "1f0c2c9a-2f1a-4a3e-9d1a-3b6c8e5d4f21",
            "en",
            ["I run a platform that settles 40 million transactions a day."],
            coverNote ?? "I run the sending platform and the tooling around it. The posting describes the same work.",
            [new KitAnswerPayload("Compensation expectation", answer ?? "Open to discuss. [CONFIRM]")],
            ["Who owns the platform roadmap?"]);
    }
}
