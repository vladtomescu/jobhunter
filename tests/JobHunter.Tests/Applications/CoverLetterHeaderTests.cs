using JobHunter.Applications;
using JobHunter.Domain;

namespace JobHunter.Tests.Applications;

/// <summary>Proves how the template's Header lines are filled from the settings: placeholders, dropped segments and lines, bold runs and the name line.</summary>
public sealed class CoverLetterHeaderTests
{
    [Fact]
    public void Render_WithEveryContactDetailSet_FillsEveryPlaceholderAndMarksTheNameLine()
    {
        IReadOnlyList<CoverLetterHeaderLine> lines = CoverLetterHeader.Render(["**{name}**", "{location} · {email} · {phone} · {linkedin}"], Settings());

        Assert.Equal(2, lines.Count);
        Assert.Equal("Test Placeholder", lines[0].Text);
        Assert.True(lines[0].IsNameLine);
        Assert.Equal("Utrecht, Netherlands · test.placeholder@example.com · +1 555 0100 · https://www.linkedin.com/in/placeholder", lines[1].Text);
        Assert.False(lines[1].IsNameLine);
    }

    [Fact]
    public void Render_WithBoldMarkers_TurnsTheMarkedTextIntoBoldRunsAndLeavesTheRestPlain()
    {
        CoverLetterHeaderLine line = Assert.Single(CoverLetterHeader.Render(["Contact: **{email}** or {phone}"], Settings()));

        Assert.Equal<CoverLetterRun>(
            [new CoverLetterRun("Contact: ", false), new CoverLetterRun("test.placeholder@example.com", true), new CoverLetterRun(" or +1 555 0100", false)],
            line.Runs);
    }

    [Fact]
    public void Render_WithAnEmptyPhone_DropsThePhoneSegmentAndKeepsTheSeparatorsBetweenTheRest()
    {
        CoverLetterHeaderLine line = Assert.Single(CoverLetterHeader.Render(["{location} · {email} · {phone} · {linkedin}"], Settings(phone: string.Empty)));

        Assert.Equal("Utrecht, Netherlands · test.placeholder@example.com · https://www.linkedin.com/in/placeholder", line.Text);
    }

    [Fact]
    public void Render_WithASegmentOfPlainTextAndAnEmptyPlaceholderLabel_KeepsThePlainTextAndDropsTheLabelledSegment()
    {
        CoverLetterHeaderLine line = Assert.Single(CoverLetterHeader.Render(["Software engineer · Phone: {phone} · {email}"], Settings(phone: " ")));

        Assert.Equal("Software engineer · test.placeholder@example.com", line.Text);
    }

    [Fact]
    public void Render_WithALineWhosePlaceholdersAreAllEmpty_DropsTheLine()
    {
        IReadOnlyList<CoverLetterHeaderLine> lines = CoverLetterHeader.Render(["**{name}**", "{phone} · {linkedin}", "{email}"], Settings(phone: string.Empty, linkedIn: string.Empty));

        Assert.Equal<string>(["Test Placeholder", "test.placeholder@example.com"], [.. lines.Select(line => line.Text)]);
    }

    [Fact]
    public void Render_WithNoNameSet_DropsTheNameLine()
    {
        IReadOnlyList<CoverLetterHeaderLine> lines = CoverLetterHeader.Render(["**{name}**", "{email}"], Settings(firstName: string.Empty, lastName: string.Empty));

        CoverLetterHeaderLine line = Assert.Single(lines);
        Assert.False(line.IsNameLine);
    }

    [Fact]
    public void Render_WithAnUnmatchedBoldMarkerOrAnUnknownPlaceholder_LeavesThemAsWritten()
    {
        CoverLetterHeaderLine line = Assert.Single(CoverLetterHeader.Render(["**{name}** · {website} · **open"], Settings()));

        Assert.Equal("Test Placeholder · {website} · **open", line.Text);
        Assert.True(line.Runs[0].Bold);
        Assert.False(line.Runs[1].Bold);
    }

    [Fact]
    public void Name_WithOnlyAFirstName_IsTheFirstNameAlone()
    {
        Assert.Equal("Test", CoverLetterHeader.Name(Settings(lastName: string.Empty)));
    }

    private static JobHunter.Domain.Settings Settings(string firstName = "Test", string lastName = "Placeholder", string phone = "+1 555 0100", string linkedIn = "https://www.linkedin.com/in/placeholder")
    {
        JobHunter.Domain.Settings settings = JobHunter.Domain.Settings.CreateDefault();
        settings.ConfigureContact(firstName, lastName, "test.placeholder@example.com", phone, "Utrecht, Netherlands", linkedIn);

        return settings;
    }
}
