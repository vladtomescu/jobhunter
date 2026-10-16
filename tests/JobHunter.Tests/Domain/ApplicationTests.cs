using JobHunter.Domain;

namespace JobHunter.Tests.Domain;

/// <summary>Covers the application aggregate's status moves and the submission record behind a move to Applied.</summary>
public sealed class ApplicationTests
{
    private static readonly DateTimeOffset SavedAt = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MarkApplied_OnASavedApplication_RecordsTheChannelTheResumeAndTheDate()
    {
        Application application = NewSavedApplication();
        DateTimeOffset appliedAt = SavedAt.AddDays(1);

        application.MarkApplied(appliedAt, ApplicationChannel.LinkedIn, "resume-v3.pdf", "applied through LinkedIn");

        Assert.Equal(ApplicationStatus.Applied, application.Status);
        Assert.Equal(ApplicationChannel.LinkedIn, application.Channel);
        Assert.Equal("resume-v3.pdf", application.CvVersion);
        Assert.Equal(appliedAt, application.AppliedAt);
        Assert.Equal(appliedAt, application.StatusChangedAt);
    }

    [Fact]
    public void MarkApplied_WhenAppliedBefore_RecordsTheNewChannelAndKeepsTheFirstAppliedAt()
    {
        Application application = NewSavedApplication();
        DateTimeOffset firstAppliedAt = SavedAt.AddDays(1);
        application.MarkApplied(firstAppliedAt, ApplicationChannel.Ats, "resume-v3.pdf", null);
        application.MoveTo(ApplicationStatus.Screening, SavedAt.AddDays(3), null);

        application.MarkApplied(SavedAt.AddDays(5), ApplicationChannel.Referral, "resume-v4.pdf", "referred after all");

        Assert.Equal(ApplicationChannel.Referral, application.Channel);
        Assert.Equal("resume-v4.pdf", application.CvVersion);
        Assert.Equal(firstAppliedAt, application.AppliedAt);
        Assert.Equal(SavedAt.AddDays(5), application.StatusChangedAt);
    }

    [Fact]
    public void MoveTo_ToAppliedAfterAMoveToAnotherStatus_KeepsTheFirstAppliedAt()
    {
        Application application = NewSavedApplication();
        application.MoveTo(ApplicationStatus.Applied, SavedAt.AddDays(1), null);
        application.MoveTo(ApplicationStatus.Withdrawn, SavedAt.AddDays(2), null);

        application.MoveTo(ApplicationStatus.Applied, SavedAt.AddDays(4), null);

        Assert.Equal(SavedAt.AddDays(1), application.AppliedAt);
    }

    [Fact]
    public void MarkApplied_AfterEachMove_AppendsOneHistoryRowWithTheStatusAndTheNote()
    {
        Application application = NewSavedApplication();

        application.MarkApplied(SavedAt.AddDays(1), ApplicationChannel.Email, null, "first mail");
        application.MoveTo(ApplicationStatus.Manager, SavedAt.AddDays(2), "call booked");
        application.MarkApplied(SavedAt.AddDays(3), ApplicationChannel.Email, null, null);

        Assert.Equal<ApplicationStatus>([ApplicationStatus.Saved, ApplicationStatus.Applied, ApplicationStatus.Manager, ApplicationStatus.Applied], application.History.Select(entry => entry.Status));
        Assert.Equal<string?>(["Saved from the inbox.", "first mail", "call booked", null], application.History.Select(entry => entry.Note));
    }

    private static Application NewSavedApplication()
    {
        return Application.Create(Guid.CreateVersion7(), ApplicationStatus.Saved, SavedAt, "Saved from the inbox.");
    }
}
