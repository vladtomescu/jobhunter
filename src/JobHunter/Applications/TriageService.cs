using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Jobs;
using JobHunter.Llm;
using JobHunter.Llm.Contracts;
using JobHunter.Settings;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Applications;

/// <summary>What taking back a pursuit did: done, or refused with the reason and nothing changed.</summary>
public sealed record UnpursueResult(string? Refusal)
{
    /// <summary>True when the application was deleted and the job went back to the inbox.</summary>
    public bool IsUnpursued => Refusal is null;
}

/// <summary>Turns a triage decision into the application record; the kit is written separately, on request.</summary>
public sealed class TriageService(IDbContextFactory<JobHunterDbContext> contextFactory, IKitWriter kitWriter, SettingsService settingsService)
{
    /// <summary>Marks the job pursued and creates its application at Saved with no kit; a job that already has an application returns it unchanged.</summary>
    public async Task<Application> PursueAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        DateTimeOffset at = DateTimeOffset.UtcNow;

        return await CreateOrGetApplicationAsync(jobId, at, cancellationToken);
    }

    /// <summary>Writes the kit for an application that has none yet, or writes it again after a failed attempt; an application whose kit is already generating or ready is returned unchanged.</summary>
    public async Task<Application> WriteKitAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        Application application = await context.Applications.AsNoTracking().SingleAsync(candidate => candidate.JobId == jobId, cancellationToken);

        if (application.KitState is KitState.Generating or KitState.Ready)
        {
            return application;
        }

        await GenerateKitAsync(jobId, application.Id, cancellationToken);

        return await ReloadApplicationAsync(application.Id, cancellationToken);
    }

    /// <summary>Marks the job skipped so that it leaves the inbox for good; no application is created.</summary>
    public async Task SkipAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        DateTimeOffset at = DateTimeOffset.UtcNow;

        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Job job = await context.Jobs.SingleAsync(candidate => candidate.Id == jobId, cancellationToken);
        job.Skip(at);

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Takes back a pursuit: deletes the application with its kit, notes, contact and history, and returns the job to the inbox untriaged, in one save; a job without an application, or whose application moved past Saved, is refused.</summary>
    public async Task<UnpursueResult> UnpursueAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Application? application = await context.Applications.FirstOrDefaultAsync(candidate => candidate.JobId == jobId, cancellationToken);
        if (application is null)
        {
            return new UnpursueResult("The job has no application to take back.");
        }

        if (application.Status != ApplicationStatus.Saved)
        {
            return new UnpursueResult($"The application has moved on to {JobDisplay.StatusLabel(application.Status)}; withdraw it on the pipeline instead.");
        }

        Job job = await context.Jobs.SingleAsync(candidate => candidate.Id == jobId, cancellationToken);
        job.ReturnToInbox();
        context.Applications.Remove(application);
        await context.SaveChangesAsync(cancellationToken);

        return new UnpursueResult(null);
    }

    private async Task<Application> ReloadApplicationAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Applications.AsNoTracking().SingleAsync(candidate => candidate.Id == applicationId, cancellationToken);
    }

    private async Task<Application> CreateOrGetApplicationAsync(Guid jobId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Application? existing = await context.Applications.FirstOrDefaultAsync(candidate => candidate.JobId == jobId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        Job job = await context.Jobs.SingleAsync(candidate => candidate.Id == jobId, cancellationToken);
        job.Pursue(at);

        Application application = Application.Create(jobId, ApplicationStatus.Saved, at, "Saved from the inbox.");
        context.Applications.Add(application);
        await context.SaveChangesAsync(cancellationToken);

        return application;
    }

    private async Task GenerateKitAsync(Guid jobId, Guid applicationId, CancellationToken cancellationToken)
    {
        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Application application = await context.Applications.SingleAsync(candidate => candidate.Id == applicationId, cancellationToken);
        application.BeginKit();
        await context.SaveChangesAsync(cancellationToken);

        Job job = await context.Jobs.AsNoTracking().SingleAsync(candidate => candidate.Id == jobId, cancellationToken);

        if (job.Score is not ScoreCard score)
        {
            application.FailKit("The job has not been scored yet, so no kit can be written.");
            await context.SaveChangesAsync(cancellationToken);

            return;
        }

        Domain.Settings settings = await settingsService.GetAsync(cancellationToken);
        KitRequest request = LlmRequests.ForKit(job, ScoreCardMapper.ToScorePayload(jobId, score), settings.KitModel);

        KitOutcome outcome;
        try
        {
            outcome = await kitWriter.WriteAsync(request, cancellationToken);
        }
        catch (Exception exception)
        {
            application.FailKit(exception.Message);
            await context.SaveChangesAsync(cancellationToken);

            return;
        }

        if (!outcome.IsSuccess || outcome.Payload is null)
        {
            application.FailKit(outcome.FailureReason ?? "The kit writer returned no usable result.");
            await context.SaveChangesAsync(cancellationToken);

            return;
        }

        ApplicationKit kit = new(
            [.. outcome.Payload.FitSummary],
            outcome.Payload.CoverNote,
            [.. outcome.Payload.CallQuestions],
            settings.ResumePdfPath,
            outcome.Payload.Language,
            DateTimeOffset.UtcNow,
            outcome.Model ?? settings.KitModel,
            [.. outcome.LintIssues])
        {
            AtsAnswers = [.. outcome.Payload.AtsAnswers.Select(answer => new AtsAnswer(answer.Question, answer.Answer))]
        };

        application.AttachKit(kit);

        if (outcome.LintIssues.Count > 0)
        {
            application.FailKit(string.Join(" ", outcome.LintIssues));
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
