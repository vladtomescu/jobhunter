using JobHunter.Prefill;

namespace JobHunter.Tests.Prefill;

public class PrefillValuesTests
{
    [Fact]
    public void ValueFor_ContactRecorded_ReturnsTheSettingsValuePerField()
    {
        PrefillValues values = PrefillValues.From(ContactSettings(), null);

        Assert.Equal("Test", values.ValueFor(PrefillField.FirstName));
        Assert.Equal("Placeholder", values.ValueFor(PrefillField.LastName));
        Assert.Equal("Test Placeholder", values.ValueFor(PrefillField.FullName));
        Assert.Equal("test.placeholder@example.com", values.ValueFor(PrefillField.Email));
        Assert.Equal("+1 555 0100", values.ValueFor(PrefillField.Phone));
        Assert.Equal("Remote, Europe", values.ValueFor(PrefillField.Location));
        Assert.Equal("https://www.linkedin.com/in/placeholder", values.ValueFor(PrefillField.LinkedIn));
    }

    [Fact]
    public void ValueFor_ContactNotEnteredYet_ReturnsNullSoTheControlIsLeftAlone()
    {
        PrefillValues values = PrefillValues.From(JobHunter.Domain.Settings.CreateDefault(), null);

        Assert.Null(values.ValueFor(PrefillField.FirstName));
        Assert.Null(values.ValueFor(PrefillField.FullName));
        Assert.Null(values.ValueFor(PrefillField.Email));
        Assert.Null(values.ValueFor(PrefillField.LinkedIn));
    }

    [Fact]
    public void ValueFor_OnlyOneNameHalfRecorded_TrimsTheFullName()
    {
        JobHunter.Domain.Settings settings = JobHunter.Domain.Settings.CreateDefault();
        settings.ConfigureContact("Test", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

        Assert.Equal("Test", PrefillValues.From(settings, null).ValueFor(PrefillField.FullName));
    }

    [Fact]
    public void ValueFor_ResumePathFromSettings_IsTheFileToAttach()
    {
        Assert.Equal(JobHunter.Domain.Settings.CreateDefault().ResumePdfPath, PrefillValues.From(JobHunter.Domain.Settings.CreateDefault(), null).ValueFor(PrefillField.Resume));
    }

    [Fact]
    public void ValueFor_NoKitYet_LeavesTheCoverLetterEmpty()
    {
        Assert.Null(PrefillValues.From(ContactSettings(), null).ValueFor(PrefillField.CoverLetter));
        Assert.Null(PrefillValues.From(ContactSettings(), "   ").ValueFor(PrefillField.CoverLetter));
    }

    [Fact]
    public void ValueFor_KitWritten_OffersItsCoverNote()
    {
        Assert.Equal("A short note.", PrefillValues.From(ContactSettings(), "A short note.").ValueFor(PrefillField.CoverLetter));
    }

    private static JobHunter.Domain.Settings ContactSettings()
    {
        JobHunter.Domain.Settings settings = JobHunter.Domain.Settings.CreateDefault();
        settings.ConfigureContact("Test", "Placeholder", "test.placeholder@example.com", "+1 555 0100", "Remote, Europe", "https://www.linkedin.com/in/placeholder");

        return settings;
    }
}
