using JobHunter.Data;
using JobHunter.Domain;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Applications;

/// <summary>Changes an application after it exists: marking it applied, moving it through the pipeline, and recording notes, contact and next action.</summary>
public sealed class ApplicationService(IDbContextFactory<JobHunterDbContext> contextFactory)
{
    /// <summary>Marks the application as submitted; when the job has no application yet, one is created at Saved first, so that "mark applied" always leaves a record behind.</summary>
    public async Task<Application> MarkAppliedAsync(Guid jobId, ApplicationChannel channel, string? cvVersion, string? note, CancellationToken cancellationToken = default)
    {
        DateTimeOffset at = DateTimeOffset.UtcNow;

        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Application? application = await context.Applications.FirstOrDefaultAsync(candidate => candidate.JobId == jobId, cancellationToken);
        if (application is null)
        {
            application = Application.Create(jobId, ApplicationStatus.Saved, at, "Application created directly.");
            context.Applications.Add(application);
        }

        application.MarkApplied(at, channel, cvVersion, note);
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

    private async Task<Application> UpdateAsync(Guid applicationId, Action<Application> change, CancellationToken cancellationToken)
    {
        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Application application = await context.Applications.SingleAsync(candidate => candidate.Id == applicationId, cancellationToken);
        change(application);
        await context.SaveChangesAsync(cancellationToken);

        return application;
    }
}
