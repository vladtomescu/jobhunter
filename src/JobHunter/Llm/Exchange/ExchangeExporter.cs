using System.Linq.Expressions;
using System.Text;
using JobHunter.Applications;
using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Llm.Contracts;
using JobHunter.Settings;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Llm.Exchange;

/// <summary>What one export left in the exchange folder.</summary>
public sealed record ExchangeExportResult(int JobsToScore, int JobsToKit, bool ResumeCopied, string Folder);

/// <summary>Writes the jobs that still need a score and the pursued jobs that still need a kit into the exchange folder, together with the resume, and builds the interview-prep input of one job.</summary>
/// <remarks>The lines carry the same fields the interface path sends, so a result produced from these files is interchangeable with one produced through the model.</remarks>
public sealed class ExchangeExporter(IDbContextFactory<JobHunterDbContext> contextFactory, SettingsService settingsService, DataPaths dataPaths)
{
    private static readonly UTF8Encoding Utf8WithoutMarker = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Writes the two job files and copies the resume, replacing whatever the folder held before.</summary>
    public async Task<ExchangeExportResult> ExportAsync(CancellationToken cancellationToken = default)
    {
        Domain.Settings settings = await settingsService.GetAsync(cancellationToken);
        string folder = dataPaths.Exchange;

        List<string> scoreLines = await BuildScoreLinesAsync(cancellationToken);

        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        List<string> kitLines = await BuildKitLinesAsync(context, cancellationToken);

        await WriteLinesAsync(Path.Combine(folder, ExchangeFiles.ToScore), scoreLines, cancellationToken);
        await WriteLinesAsync(Path.Combine(folder, ExchangeFiles.ToKit), kitLines, cancellationToken);

        bool resumeCopied = await CopyResumeAsync(settings.ResumeMarkdownPath, Path.Combine(folder, ExchangeFiles.Resume), cancellationToken);

        return new ExchangeExportResult(scoreLines.Count, kitLines.Count, resumeCopied, folder);
    }

    /// <summary>One to-score line for every job that still needs a score, newest posting first; the export writes exactly these lines.</summary>
    public async Task<List<string>> BuildScoreLinesAsync(CancellationToken cancellationToken = default)
    {
        return await BuildScoreLinesAsync(job => job.Prefilter == PrefilterState.Passed && job.Scoring != ScoringState.Scored && job.IsActive, cancellationToken);
    }

    /// <summary>One to-score line for each named job that is stored, whatever its prefilter verdict, activity or scoring state, as Score again on the job page allows; an id that is not stored is left out.</summary>
    public async Task<List<string>> BuildScoreLinesForJobsAsync(IReadOnlyCollection<Guid> jobIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobIds);

        return await BuildScoreLinesAsync(job => jobIds.Contains(job.Id), cancellationToken);
    }

    /// <summary>The interview-prep input for one stored job, whatever its prefilter verdict, scoring state or application status; null when no job is stored under the identifier. Nothing is changed.</summary>
    public async Task<PrepExchangeInput?> BuildPrepInputAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        Job? job = await context.Jobs.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == jobId, cancellationToken);
        if (job is null)
        {
            return null;
        }

        Application? application = await context.Applications.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.JobId == jobId, cancellationToken);
        Domain.Settings settings = await settingsService.GetAsync(cancellationToken);
        string? resume = await ResumeMarkdown.ReadAsync(settings.ResumeMarkdownPath, cancellationToken);

        return PrepExchangeInput.From(job, application, settings, resume);
    }

    private async Task<List<string>> BuildScoreLinesAsync(Expression<Func<Job, bool>> selection, CancellationToken cancellationToken)
    {
        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        List<Job> jobs = await context.Jobs
            .AsNoTracking()
            .Where(selection)
            .OrderByDescending(job => job.PostedAt)
            .ToListAsync(cancellationToken);

        return [.. jobs.Select(job => LlmJson.ToLine(ScoreExchangeLine.From(LlmRequests.ForScore(job, ExchangeFiles.Model))))];
    }

    private static async Task<List<string>> BuildKitLinesAsync(JobHunterDbContext context, CancellationToken cancellationToken)
    {
        List<Application> pending = await context.Applications
            .AsNoTracking()
            .Where(application => application.KitState == KitState.None || application.KitState == KitState.Failed)
            .ToListAsync(cancellationToken);

        List<Guid> jobIds = [.. pending.Select(application => application.JobId)];
        if (jobIds.Count == 0)
        {
            return [];
        }

        List<Job> jobs = await context.Jobs
            .AsNoTracking()
            .Where(job => jobIds.Contains(job.Id))
            .ToListAsync(cancellationToken);

        List<string> lines = [];
        foreach (Job job in jobs)
        {
            if (job.Score is ScoreCard score)
            {
                ScorePayload payload = ScoreCardMapper.ToScorePayload(job.Id, score);
                lines.Add(LlmJson.ToLine(KitExchangeLine.From(LlmRequests.ForKit(job, payload, ExchangeFiles.Model))));
            }
        }

        return lines;
    }

    private static async Task WriteLinesAsync(string path, List<string> lines, CancellationToken cancellationToken)
    {
        string content = lines.Count == 0 ? string.Empty : string.Join('\n', lines) + '\n';

        await File.WriteAllTextAsync(path, content, Utf8WithoutMarker, cancellationToken);
    }

    private static async Task<bool> CopyResumeAsync(string? resumeMarkdownPath, string destination, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(resumeMarkdownPath) || !File.Exists(resumeMarkdownPath))
        {
            return false;
        }

        await File.WriteAllTextAsync(destination, await File.ReadAllTextAsync(resumeMarkdownPath, cancellationToken), Utf8WithoutMarker, cancellationToken);

        return true;
    }
}
