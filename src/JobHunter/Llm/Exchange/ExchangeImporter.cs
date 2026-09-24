using System.Globalization;
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
    private static readonly string[] AllowedLevelGuesses = ["junior", "mid", "senior", "staff", "principal", "lead", "unknown"];
    private static readonly string[] AllowedRemotePolicies = ["remote", "hybrid", "onsite", "unknown"];
    private static readonly string[] AllowedEmploymentTypes = ["b2b", "employment", "either", "unknown"];
    private static readonly string[] AllowedCompPeriods = ["hour", "day", "month", "year"];
    private static readonly string[] AllowedBlockingUnknowns = ["end_client", "b2b", "timezone", "us_authorization"];

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

    /// <summary>Checks the score payload's string-valued fields against the vocabulary <c>prompts/schemas/score.schema.json</c> defines for them, so a value the classifier would not recognize is refused here instead of silently taking the wrong branch.</summary>
    private static string? OutOfVocabulary(ScorePayload payload)
    {
        (string Name, string Value, string[] Allowed)[] fields =
        [
            ("level_guess", payload.Facts.LevelGuess, AllowedLevelGuesses),
            ("remote_policy", payload.Facts.RemotePolicy, AllowedRemotePolicies),
            ("employment_type", payload.Facts.EmploymentType, AllowedEmploymentTypes)
        ];

        foreach ((string name, string value, string[] allowed) in fields)
        {
            if (Array.IndexOf(allowed, value) < 0)
            {
                return $"{name} is \"{value}\", which is not one of: {string.Join(", ", allowed)}";
            }
        }

        if (payload.Facts.Comp.Period is string period && Array.IndexOf(AllowedCompPeriods, period) < 0)
        {
            return $"comp.period is \"{period}\", which is not one of: {string.Join(", ", AllowedCompPeriods)}";
        }

        foreach (string code in payload.BlockingUnknowns)
        {
            if (Array.IndexOf(AllowedBlockingUnknowns, code) < 0)
            {
                return $"blocking_unknowns contains \"{code}\", which is not one of: {string.Join(", ", AllowedBlockingUnknowns)}";
            }
        }

        return null;
    }

    private static async Task<string[]> ReadLinesAsync(string path, CancellationToken cancellationToken)
    {
        return File.Exists(path) ? await File.ReadAllLinesAsync(path, cancellationToken) : [];
    }

    /// <summary>Rewrites the file down to whatever was refused, so it can be fixed and retried, or renames it out of the way once every line in it imported, so a later import cannot apply it again.</summary>
    private static async Task ConsumeInputFileAsync(string path, IReadOnlyList<string> unresolvedLines, CancellationToken cancellationToken)
    {
        if (unresolvedLines.Count > 0)
        {
            await File.WriteAllTextAsync(path, string.Join('\n', unresolvedLines) + '\n', cancellationToken);

            return;
        }

        string directory = Path.GetDirectoryName(path)!;
        File.Move(path, Path.Combine(directory, ConsumedFileName(Path.GetFileName(path), DateTimeOffset.UtcNow)));
    }

    /// <summary>The name a fully-imported file is renamed to: the original name with an <c>imported-&lt;timestamp&gt;</c> marker before the extension, so the result stays on disk without being mistaken for pending work.</summary>
    private static string ConsumedFileName(string fileName, DateTimeOffset importedAt)
    {
        return $"{Path.GetFileNameWithoutExtension(fileName)}.imported-{importedAt.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture)}{Path.GetExtension(fileName)}";
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
        string path = Path.Combine(dataPaths.Exchange, ExchangeFiles.Scored);
        string[] lines = await ReadLinesAsync(path, cancellationToken);
        if (lines.Length == 0)
        {
            return 0;
        }

        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        DateTimeOffset scoredAt = DateTimeOffset.UtcNow;
        int imported = 0;
        List<string> unresolvedLines = [];

        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            void Reject(string reason)
            {
                rejections.Add(new ExchangeLineRejection(ExchangeFiles.Scored, index + 1, reason));
                unresolvedLines.Add(line);
            }

            LlmJsonResult<ScorePayload> parsed = LlmJson.Read<ScorePayload>(line);
            if (parsed.Payload is not ScorePayload payload)
            {
                Reject($"the line does not match the score schema: {parsed.Error}");

                continue;
            }

            if (!Guid.TryParse(payload.JobId, out Guid jobId))
            {
                Reject($"job_id {payload.JobId} is not an identifier");

                continue;
            }

            if (OutOfRange(payload.Scores) is string outOfRange)
            {
                Reject(outOfRange);

                continue;
            }

            if (OutOfVocabulary(payload) is string outOfVocabulary)
            {
                Reject(outOfVocabulary);

                continue;
            }

            Job? job = await context.Jobs.FirstOrDefaultAsync(candidate => candidate.Id == jobId, cancellationToken);
            if (job is null)
            {
                Reject($"no job is stored under the identifier {jobId}");

                continue;
            }

            await scoreApplier.ApplyAsync(job, payload, ExchangeFiles.Model, settings, scoredAt, cancellationToken);
            imported++;
        }

        await context.SaveChangesAsync(cancellationToken);
        await ConsumeInputFileAsync(path, unresolvedLines, cancellationToken);

        return imported;
    }

    private async Task<int> ImportKitsAsync(Domain.Settings settings, List<ExchangeLineRejection> rejections, CancellationToken cancellationToken)
    {
        string path = Path.Combine(dataPaths.Exchange, ExchangeFiles.Kits);
        string[] lines = await ReadLinesAsync(path, cancellationToken);
        if (lines.Length == 0)
        {
            return 0;
        }

        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);
        DateTimeOffset writtenAt = DateTimeOffset.UtcNow;
        int imported = 0;
        List<string> unresolvedLines = [];

        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            void Reject(string reason)
            {
                rejections.Add(new ExchangeLineRejection(ExchangeFiles.Kits, index + 1, reason));
                unresolvedLines.Add(line);
            }

            LlmJsonResult<KitPayload> parsed = LlmJson.Read<KitPayload>(line);
            if (parsed.Payload is not KitPayload payload)
            {
                Reject($"the line does not match the kit schema: {parsed.Error}");

                continue;
            }

            if (!Guid.TryParse(payload.JobId, out Guid jobId))
            {
                Reject($"job_id {payload.JobId} is not an identifier");

                continue;
            }

            IReadOnlySet<string> acceptedLanguages = CandidateProfile.ReadLanguages(settings.AcceptedLanguages);
            if (!acceptedLanguages.Contains(payload.Language))
            {
                Reject($"the language {payload.Language} is not one of the accepted languages: {string.Join(", ", acceptedLanguages)}");

                continue;
            }

            if (payload.FitSummary.Count == 0 || string.IsNullOrWhiteSpace(payload.CoverNote))
            {
                Reject("the kit has no fit summary or no cover note");

                continue;
            }

            Application? application = await context.Applications.FirstOrDefaultAsync(candidate => candidate.JobId == jobId, cancellationToken);
            if (application is null)
            {
                Reject($"no application is open for the job {jobId}, so there is nothing to attach the kit to");

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
        await ConsumeInputFileAsync(path, unresolvedLines, cancellationToken);

        return imported;
    }
}
