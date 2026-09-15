using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Llm.Contracts;
using JobHunter.Pipeline;
using JobHunter.Settings;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Llm.Exchange;

/// <summary>One line that was not imported, with the file it sits in, its position and the reason it was refused.</summary>
public sealed record ExchangeLineRejection(string File, int LineNumber, string Reason);

/// <summary>What one import stored and what it refused.</summary>
public sealed record ExchangeImportResult(int ScoresImported, int KitsImported, IReadOnlyList<ExchangeLineRejection> Rejections);

/// <summary>Reads the scores and the kits produced outside the application and stores them exactly as the interface path would.</summary>
/// <remarks>Scores go through the same score applier the refresh uses, so the class is computed in code on both paths and the model is recorded as the repository skill.</remarks>
public sealed class ExchangeImporter(IDbContextFactory<JobHunterDbContext> contextFactory, SettingsService settingsService, ScoreApplier scoreApplier, DataPaths dataPaths)
{
    /// <summary>Imports every line of both result files, refusing one line at a time rather than the whole file.</summary>
    public async Task<ExchangeImportResult> ImportAsync(CancellationToken cancellationToken = default)
    {
        Domain.Settings settings = await settingsService.GetAsync(cancellationToken);
        List<ExchangeLineRejection> rejections = [];

        int scores = await ImportScoresAsync(settings, rejections, cancellationToken);
        int kits = await ImportKitsAsync(settings, rejections, cancellationToken);

        return new ExchangeImportResult(scores, kits, rejections);
    }

    private static string? OutOfRange(ScoreDimensionsPayload scores)
    {
        (string Name, int Value)[] dimensions =
        [
            ("niche", scores.Niche),
            ("level", scores.Level),
            ("stack", scores.Stack),
            ("remote_timezone", scores.RemoteTimezone),
            ("contract_form", scores.ContractForm),
            ("comp_signal", scores.CompSignal),
            ("company_signal", scores.CompanySignal)
        ];

        foreach ((string name, int value) in dimensions)
        {
            if (value is < 0 or > 2)
            {
                return $"the score {name} is {value}, which is outside the range 0 to 2";
            }
        }

        return null;
    }

    private static async Task<string[]> ReadLinesAsync(string path, CancellationToken cancellationToken)
    {
        return File.Exists(path) ? await File.ReadAllLinesAsync(path, cancellationToken) : [];
    }

    private static ApplicationKit BuildKit(KitPayload payload, Domain.Settings settings, DateTimeOffset writtenAt, IReadOnlyList<string> issues)
    {
        return new ApplicationKit(
            [.. payload.FitSummary],
            payload.CoverNote,
            [.. payload.CallQuestions],
            settings.ResumePdfPath,
            payload.Language,
            writtenAt,
            ExchangeFiles.Model,
            [.. issues])
        {
            AtsAnswers = [.. payload.AtsAnswers.Select(answer => new AtsAnswer(answer.Question, answer.Answer))]
        };
    }

    private async Task<int> ImportScoresAsync(Domain.Settings settings, List<ExchangeLineRejection> rejections, CancellationToken cancellationToken)
    {
        string[] lines = await ReadLinesAsync(Path.Combine(dataPaths.Exchange, ExchangeFiles.Scored), cancellationToken);
        if (lines.Length == 0)
        {
            return 0;
        }

        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        DateTimeOffset scoredAt = DateTimeOffset.UtcNow;
        int imported = 0;

        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            LlmJsonResult<ScorePayload> parsed = LlmJson.Read<ScorePayload>(line);
            if (parsed.Payload is not ScorePayload payload)
            {
                rejections.Add(new ExchangeLineRejection(ExchangeFiles.Scored, index + 1, $"the line does not match the score schema: {parsed.Error}"));

                continue;
            }

            if (!Guid.TryParse(payload.JobId, out Guid jobId))
            {
                rejections.Add(new ExchangeLineRejection(ExchangeFiles.Scored, index + 1, $"job_id {payload.JobId} is not an identifier"));

                continue;
            }

            if (OutOfRange(payload.Scores) is string outOfRange)
            {
                rejections.Add(new ExchangeLineRejection(ExchangeFiles.Scored, index + 1, outOfRange));

                continue;
            }

            Job? job = await context.Jobs.FirstOrDefaultAsync(candidate => candidate.Id == jobId, cancellationToken);
            if (job is null)
            {
                rejections.Add(new ExchangeLineRejection(ExchangeFiles.Scored, index + 1, $"no job is stored under the identifier {jobId}"));

                continue;
            }

            await scoreApplier.ApplyAsync(job, payload, ExchangeFiles.Model, settings, scoredAt, cancellationToken);
            imported++;
        }

        await context.SaveChangesAsync(cancellationToken);

        return imported;
    }

    private async Task<int> ImportKitsAsync(Domain.Settings settings, List<ExchangeLineRejection> rejections, CancellationToken cancellationToken)
    {
        string[] lines = await ReadLinesAsync(Path.Combine(dataPaths.Exchange, ExchangeFiles.Kits), cancellationToken);
        if (lines.Length == 0)
        {
            return 0;
        }

        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        DateTimeOffset writtenAt = DateTimeOffset.UtcNow;
        int imported = 0;

        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            LlmJsonResult<KitPayload> parsed = LlmJson.Read<KitPayload>(line);
            if (parsed.Payload is not KitPayload payload)
            {
                rejections.Add(new ExchangeLineRejection(ExchangeFiles.Kits, index + 1, $"the line does not match the kit schema: {parsed.Error}"));

                continue;
            }

            if (!Guid.TryParse(payload.JobId, out Guid jobId))
            {
                rejections.Add(new ExchangeLineRejection(ExchangeFiles.Kits, index + 1, $"job_id {payload.JobId} is not an identifier"));

                continue;
            }

            if (payload.Language is not "en")
            {
                rejections.Add(new ExchangeLineRejection(ExchangeFiles.Kits, index + 1, $"the language {payload.Language} is not en"));

                continue;
            }

            if (payload.FitSummary.Count == 0 || string.IsNullOrWhiteSpace(payload.CoverNote))
            {
                rejections.Add(new ExchangeLineRejection(ExchangeFiles.Kits, index + 1, "the kit has no fit summary or no cover note"));

                continue;
            }

            Application? application = await context.Applications.FirstOrDefaultAsync(candidate => candidate.JobId == jobId, cancellationToken);
            if (application is null)
            {
                rejections.Add(new ExchangeLineRejection(ExchangeFiles.Kits, index + 1, $"no application is open for the job {jobId}, so there is nothing to attach the kit to"));

                continue;
            }

            IReadOnlyList<string> issues = KitLint.Inspect(payload);
            application.AttachKit(BuildKit(payload, settings, writtenAt, issues));

            if (issues.Count > 0)
            {
                application.FailKit(string.Join(" ", issues));
            }

            imported++;
        }

        await context.SaveChangesAsync(cancellationToken);

        return imported;
    }
}
