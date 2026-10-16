using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Settings;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Applications;

/// <summary>Changes an application after it exists: moving it through the pipeline, and recording notes, contact and next action.</summary>
public sealed class ApplicationService(IDbContextFactory<JobHunterDbContext> contextFactory, SettingsService settingsService)
{
    /// <summary>Moves the job's application to the status; when the job has no application yet, one is created at Saved first, so that a status chosen on the job page always leaves a record behind, with no kit and the job's triage untouched. A move to Applied records the channel and the file name of the resume in Settings; the channel means nothing to any other status.</summary>
    public async Task<Application> ChangeJobStatusAsync(Guid jobId, ApplicationStatus status, ApplicationChannel channel, string? note, CancellationToken cancellationToken = default)
    {
        DateTimeOffset at = DateTimeOffset.UtcNow;

        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Application? application = await context.Applications.FirstOrDefaultAsync(candidate => candidate.JobId == jobId, cancellationToken);
        if (application is null)
        {
            application = Application.Create(jobId, ApplicationStatus.Saved, at, "Application created directly.");
            context.Applications.Add(application);
        }

        if (status == ApplicationStatus.Applied)
        {
            application.MarkApplied(at, channel, await ReadResumeVersionAsync(cancellationToken), note);
        }
        else
        {
            application.MoveTo(status, at, note);
        }

        await context.SaveChangesAsync(cancellationToken);

        return application;
    }

    /// <summary>Moves the application to another status; any transition is allowed and each one appends a history row.</summary>
    public Task<Application> ChangeStatusAsync(Guid applicationId, ApplicationStatus status, string? note, CancellationToken cancellationToken = default)
    {
        return UpdateAsync(applicationId, application => application.MoveTo(status, DateTimeOffset.UtcNow, note), cancellationToken);
    }

    /// <summary>Appends a note to the application timeline.</summary>
    public Task<Application> AddNoteAsync(Guid applicationId, string text, CancellationToken cancellationToken = default)
    {
        return UpdateAsync(applicationId, application => application.AddNote(text, DateTimeOffset.UtcNow), cancellationToken);
    }

    /// <summary>Records the contact person on the application, or clears it when no name is given.</summary>
    public Task<Application> SetContactAsync(Guid applicationId, string? name, string? role, string? link, CancellationToken cancellationToken = default)
    {
        return UpdateAsync(applicationId, application => application.RecordContact(name, role, link), cancellationToken);
    }

    /// <summary>Records the next action and its due date, or clears it when no action is given.</summary>
    public Task<Application> SetNextActionAsync(Guid applicationId, string? action, DateOnly? due, CancellationToken cancellationToken = default)
    {
        return UpdateAsync(applicationId, application => application.PlanNextAction(action, due), cancellationToken);
    }

    /// <summary>Records what was discussed about compensation.</summary>
    public Task<Application> SetCompDiscussedAsync(Guid applicationId, string? compDiscussed, CancellationToken cancellationToken = default)
    {
        return UpdateAsync(applicationId, application => application.RecordCompDiscussed(compDiscussed), cancellationToken);
    }

    private async Task<string?> ReadResumeVersionAsync(CancellationToken cancellationToken)
    {
        Domain.Settings settings = await settingsService.GetAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(settings.ResumePdfPath) ? null : Path.GetFileName(settings.ResumePdfPath);
    }

    private async Task<Application> UpdateAsync(Guid applicationId, Action<Application> change, CancellationToken cancellationToken)
    {
        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Application application = await context.Applications.SingleAsync(candidate => candidate.Id == applicationId, cancellationToken);
        change(application);
        await context.SaveChangesAsync(cancellationToken);

        return application;
    }
}
