using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Pipeline;
using Microsoft.EntityFrameworkCore;

namespace JobHunter.Jobs;

/// <summary>What the skills page counts over and which skills it lists; every value left unset widens the jobs counted or the skills listed.</summary>
public sealed class SkillListFilter
{
    /// <summary>The one class a counted job carries, or any class when null.</summary>
    public JobClass? Class { get; set; }

    /// <summary>The least pay per year in the base currency a counted job offers, read as the top of its range or, without one, the bottom; a job of unknown pay is left out once a floor is set.</summary>
    public decimal? PayFloorPerYear { get; set; }

    /// <summary>How many days back a counted job was posted, or first seen when the posting gives no date, the same age the job lists show.</summary>
    public int? PostedWithinDays { get; set; }

    /// <summary>Leaves out the jobs located in the United States, the ones that pay in US dollars without a place in Europe or open worldwide, and the ones that require United States work authorization.</summary>
    public bool ExcludeUnitedStates { get; set; }

    /// <summary>The fewest counted jobs a listed skill is asked for by, or every skill of the catalog when null; the counts and shares still run over all the counted jobs.</summary>
    public int? MinimumJobCount { get; set; }
}

/// <summary>One skill of the catalog with how often the counted jobs ask for it: in how many and what share of them, how many lean on it as primary, required or nice to have, and the median pay of the ones that state pay.</summary>
public sealed record SkillRow(
    int Id,
    string Name,
    SkillCategory Category,
    CvMark? CvMark,
    CvMarkSetBy? CvMarkSetBy,
    int JobCount,
    double Share,
    int PrimaryCount,
    int RequiredCount,
    int NiceToHaveCount,
    decimal? MedianPayPerYear);

/// <summary>The skills page: every skill of the catalog, the most asked for first, and how many jobs the counts run over.</summary>
public sealed record SkillList(IReadOnlyList<SkillRow> Rows, int JobsWithSkills);

/// <summary>Reads the skill catalog with the market counts the skills page shows, resolving the skill names on the score cards through the catalog.</summary>
public sealed class SkillQueryService(IDbContextFactory<JobHunterDbContext> contextFactory)
{
    private const string Primary = "primary";
    private const string Required = "required";
    private const string UnitedStatesDollar = "USD";
    private const int PrimaryRank = 0;
    private const int RequiredRank = 1;
    private const int NiceToHaveRank = 2;

    /// <summary>Every skill of the catalog with its counts over the scored jobs the filter keeps, the most asked for first, then by name; a skill no counted job names is listed with zero, so it can still be marked and merged, unless a minimum job count leaves it out.</summary>
    /// <remarks>The skills sit in the score card JSON, so the jobs are narrowed in the query and the skills are counted in memory; the United States are read from the place text, so they are left out in memory too. A job counts a skill once whatever names it went by, under the strongest importance it gave the skill.</remarks>
    public async Task<SkillList> GetSkillsAsync(SkillListFilter filter, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using JobHunterDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken);

        List<Skill> skills = await context.Skills.AsNoTracking().ToListAsync(cancellationToken);
        SkillCatalog catalog = new(skills);

        List<CountedJob> jobs = await Counted(context, filter, now)
            .Select(job => new CountedJob(job.Score!, job.CompMaxPerYear ?? job.CompMinPerYear, job.CompCurrency, job.LocationText, job.RegionText, job.CountryIso))
            .ToListAsync(cancellationToken);
        CountedJob[] jobsWithSkills = [.. jobs.Where(job => job.Score.Skills.Count > 0 && !(filter.ExcludeUnitedStates && IsInUnitedStates(job)))];
        ILookup<Skill, AskedSkill> askedBySkill = jobsWithSkills.SelectMany(job => AskedSkills(job, catalog)).ToLookup(asked => asked.Skill);

        SkillRow[] rows =
        [
            .. skills
                .Select(skill => ToRow(skill, [.. askedBySkill[skill]], jobsWithSkills.Length))
                .Where(row => row.JobCount >= (filter.MinimumJobCount ?? 0))
                .OrderByDescending(row => row.JobCount)
                .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
        ];

        return new SkillList(rows, jobsWithSkills.Length);
    }

    /// <summary>The skills one job asks for, resolved through the catalog, each once under the strongest importance the job gave it; a name the catalog does not carry is left out.</summary>
    private static IEnumerable<AskedSkill> AskedSkills(CountedJob job, SkillCatalog catalog)
    {
        return job.Score.Skills
            .Select(named => (Skill: catalog.Resolve(named.Name), Rank: ImportanceRank(named.Importance)))
            .Where(entry => entry.Skill is not null)
            .GroupBy(entry => entry.Skill!)
            .Select(group => new AskedSkill(group.Key, group.Min(entry => entry.Rank), job.PayPerYear));
    }

    private static SkillRow ToRow(Skill skill, AskedSkill[] asked, int jobsWithSkills)
    {
        return new SkillRow(
            skill.Id,
            skill.Name,
            skill.Category,
            skill.CvMark,
            skill.CvMarkSetBy,
            asked.Length,
            jobsWithSkills == 0 ? 0 : (double)asked.Length / jobsWithSkills,
            asked.Count(entry => entry.Rank == PrimaryRank),
            asked.Count(entry => entry.Rank == RequiredRank),
            asked.Count(entry => entry.Rank == NiceToHaveRank),
            Median([.. asked.Where(entry => entry.PayPerYear is not null).Select(entry => entry.PayPerYear!.Value)]));
    }

    /// <summary>The scored jobs the filter keeps, narrowed in the query as far as a column can tell; the United States place test runs in memory.</summary>
    private static IQueryable<Job> Counted(JobHunterDbContext context, SkillListFilter filter, DateTimeOffset now)
    {
        IQueryable<Job> jobs = context.Jobs.AsNoTracking().Where(job => job.Scoring == ScoringState.Scored && job.Score != null);

        if (filter.Class is JobClass jobClass)
        {
            jobs = jobs.Where(job => job.Class == jobClass);
        }

        if (filter.PayFloorPerYear is decimal payFloor)
        {
            jobs = jobs.Where(job => (job.CompMaxPerYear ?? job.CompMinPerYear) >= payFloor);
        }

        if (filter.PostedWithinDays is int days and > 0)
        {
            DateTimeOffset cutoff = now.AddDays(-days);
            jobs = jobs.Where(job => (job.PostedAt ?? job.FirstSeenAt) >= cutoff);
        }

        if (filter.ExcludeUnitedStates)
        {
            jobs = jobs.Where(job => !job.Flags.Contains(JobFlag.WA));
        }

        return jobs;
    }

    /// <summary>True when the job's place reads as the United States, or when it pays in US dollars without a place in Europe or open worldwide, as a bare "Remote" or a US city the source filed under another country does.</summary>
    private static bool IsInUnitedStates(CountedJob job)
    {
        RegionScope scope = GeographyRules.ReadRegionScope(job.LocationText, job.RegionText, job.CountryIso);

        return scope == RegionScope.UnitedStates
            || (string.Equals(job.PayCurrency, UnitedStatesDollar, StringComparison.OrdinalIgnoreCase) && scope is not (RegionScope.Europe or RegionScope.Worldwide));
    }

    /// <summary>How strongly a posting leans on a skill, strongest first: primary, then required, then nice to have.</summary>
    private static int ImportanceRank(string importance)
    {
        return SkillCatalog.Normalize(importance) switch
        {
            Primary => PrimaryRank,
            Required => RequiredRank,
            _ => NiceToHaveRank
        };
    }

    private static decimal? Median(decimal[] values)
    {
        if (values.Length == 0)
        {
            return null;
        }

        Array.Sort(values);
        int middle = values.Length / 2;

        return values.Length % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2;
    }

    /// <summary>A counted job as far as the counts read it: its score card, its pay per year (the top of its range or, without one, the bottom) with the currency the posting stated, and its place.</summary>
    private sealed record CountedJob(ScoreCard Score, decimal? PayPerYear, string? PayCurrency, string? LocationText, string? RegionText, string? CountryIso);

    /// <summary>One skill one job asks for, with the strongest importance the job gave it and the job's pay per year.</summary>
    private sealed record AskedSkill(Skill Skill, int Rank, decimal? PayPerYear);
}
