using System.Globalization;
using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Pipeline;
using JobHunter.Sources;
using JobHunter.Sources.Dataset;
using Microsoft.Extensions.DependencyInjection;
using Parquet;
using Parquet.Schema;

namespace JobHunter.Tests.Sources;

/// <summary>Proves the dataset reader against a parquet slice written by the test with the column names and types of the live schema.</summary>
public sealed class DatasetJobReaderTests : IDisposable
{
    private static readonly DateTimeOffset NotBefore = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly string workFolder = Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ReadAsync_OverASliceOfMixedRows_KeepsOnlyRecentAllowedTitlesInAnAllowedLanguage()
    {
        string slicePath = await WriteSliceAsync();

        IReadOnlyList<RawJob> jobs = await ReadAllAsync(slicePath);

        Assert.Equal<string>(
            ["greenhouse:1001", "greenhouse:1002", "greenhouse:1007", "greenhouse:1008", "https://jobs.lever.co/beta/9"],
            jobs.Select(job => job.SourceId).ToArray());
    }

    [Fact]
    public async Task ReadAsync_ForARowWithEveryColumnSet_MapsEveryColumnTheRequirementsName()
    {
        string slicePath = await WriteSliceAsync();

        RawJob job = (await ReadAllAsync(slicePath)).First();

        Assert.Equal(JobSourceKind.Dataset, job.Source);
        Assert.Equal("Senior Backend Engineer", job.Title);
        Assert.Equal("Acme", job.Company);
        Assert.Null(job.CompanyUrl);
        Assert.Equal("https://boards.greenhouse.io/acme/jobs/1", job.PostingUrl);
        Assert.Equal("https://boards.greenhouse.io/acme/jobs/1/apply", job.ApplyUrl);
        Assert.Equal("## About\nWe run **.NET** services.", job.DescriptionRaw);
        Assert.Equal("Remote, Europe", job.LocationText);
        Assert.Equal("DE", job.CountryIso);
        Assert.Equal("EMEA", job.RegionText);
        Assert.True(job.IsRemote);
        Assert.Null(job.Language);
        Assert.Equal(90_000m, job.CompMin);
        Assert.Equal(120_000m, job.CompMax);
        Assert.Equal("EUR", job.CompCurrency);
        Assert.Equal(CompPeriod.Year, job.CompPeriod);
        Assert.Null(job.CompSummary);
        Assert.Equal("FULL_TIME", job.EmploymentType);
        Assert.Equal("greenhouse", job.Ats);
        Assert.Equal<string>(["Engineering"], job.Tags);
        Assert.Equal(new DateTimeOffset(2026, 9, 11, 10, 23, 28, 662, TimeSpan.Zero), job.PostedAt);
    }

    [Fact]
    public async Task ReadAsync_ForARowWithoutAPostingDate_KeepsTheRowWithAnUnknownDate()
    {
        string slicePath = await WriteSliceAsync();

        RawJob job = (await ReadAllAsync(slicePath)).Single(candidate => candidate.SourceId == "greenhouse:1002");

        Assert.Null(job.PostedAt);
        Assert.Null(job.IsRemote);
        Assert.Null(job.CompMin);
        Assert.Null(job.CompPeriod);
    }

    [Fact]
    public async Task ReadAsync_ForARowWithoutAnApplicantTrackingIdentifier_FallsBackToTheUrlAndMapsHourlyPay()
    {
        string slicePath = await WriteSliceAsync();

        RawJob job = (await ReadAllAsync(slicePath)).Single(candidate => candidate.SourceId == "https://jobs.lever.co/beta/9");

        Assert.Equal("lever", job.Ats);
        Assert.Equal(70m, job.CompMin);
        Assert.Equal(90m, job.CompMax);
        Assert.Equal("USD", job.CompCurrency);
        Assert.Equal(CompPeriod.Hour, job.CompPeriod);
        Assert.False(job.IsRemote);
        Assert.Null(job.CountryIso);
        Assert.Empty(job.Tags);
        Assert.Equal(new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero), job.PostedAt);
    }

    [Fact]
    public async Task ReadAsync_ForARowWhosePeriodIsSpelledOut_ReadsItThroughTheSharedPeriodParser()
    {
        string slicePath = await WriteSliceAsync();

        RawJob job = (await ReadAllAsync(slicePath)).Single(candidate => candidate.SourceId == "greenhouse:1007");

        Assert.Equal(8_000m, job.CompMin);
        Assert.Equal(10_000m, job.CompMax);
        Assert.Equal("EUR", job.CompCurrency);
        Assert.Equal(CompPeriod.Month, job.CompPeriod);
    }

    [Fact]
    public async Task ReadAsync_OverASliceOfSeveralRowGroups_YieldsRowsFromEveryGroup()
    {
        string slicePath = await WriteSliceAsync();

        await using FileStream stream = File.OpenRead(slicePath);
        await using ParquetReader reader = await ParquetReader.CreateAsync(stream);

        Assert.Equal(2, reader.RowGroupCount);

        IReadOnlyList<RawJob> jobs = await ReadAllAsync(slicePath);

        Assert.Equal(2, jobs.Count(job => job.SourceId is "greenhouse:1001" or "greenhouse:1002"));
        Assert.Equal(3, jobs.Count(job => job.SourceId is "greenhouse:1007" or "greenhouse:1008" or "https://jobs.lever.co/beta/9"));
    }

    [Fact]
    public async Task ReadAsync_ForASliceWhoseColumnsCarryOtherTypes_ReadsEachColumnByTheTypeItIsStoredAs()
    {
        string slicePath = await WriteMixedTypeSliceAsync();

        IReadOnlyList<RawJob> jobs = await ReadAllAsync(slicePath);

        RawJob paid = jobs.Single(job => job.SourceId == "greenhouse:1001");
        RawJob unpaid = jobs.Single(job => job.SourceId == "greenhouse:1002");

        Assert.Equal(2, jobs.Count);
        Assert.Equal(90_000m, paid.CompMin);
        Assert.Equal(120_500.75m, paid.CompMax);
        Assert.Equal("EUR", paid.CompCurrency);
        Assert.Equal(CompPeriod.Year, paid.CompPeriod);
        Assert.True(paid.IsRemote);
        Assert.Equal("Senior Backend Engineer", paid.Title);
        Assert.Equal(new DateTimeOffset(2026, 9, 11, 10, 23, 28, 662, TimeSpan.Zero), paid.PostedAt);
        Assert.Null(unpaid.CompMin);
        Assert.Null(unpaid.CompMax);
        Assert.False(unpaid.IsRemote);
    }

    [Fact]
    public void Parse_ForAManifestDocument_ReadsTheSlicePerApplicantTrackingSystem()
    {
        DatasetManifest manifest = ManifestClient.Parse(
            """
            {
              "all": { "parquet": "https://storage.example.com/all.parquet" },
              "by_ats": {
                "manfred": {
                  "csv": "https://storage.example.com/manfred/jobs.csv",
                  "parquet": "https://storage.example.com/manfred/jobs.parquet",
                  "parquet_sha256": "13957b889dde64cc266985175ff3e3840f102f60b13e645e6a43d683abe5e242",
                  "parquet_size_bytes": 61597,
                  "rows": 18,
                  "sha256": "a4fa0974eba3854295e2f4e12f70c4cd7ecd3f2a22baf1b2d21fe5059a81b0fc",
                  "size_bytes": 145155
                }
              },
              "companies": { "parquet": "https://storage.example.com/companies.parquet" },
              "generated_at": "2026-09-14T14:30:04.720006+00:00",
              "stats": { "ats_count": 63, "total_jobs": 5150906 },
              "version": "2.0"
            }
            """);

        DatasetSlice slice = manifest.Slices["manfred"];

        Assert.Equal("2.0", manifest.Version);
        Assert.Equal(DateTimeOffset.Parse("2026-09-14T14:30:04.720006+00:00", CultureInfo.InvariantCulture), manifest.GeneratedAt);
        Assert.Equal("https://storage.example.com/manfred/jobs.parquet", slice.ParquetUrl);
        Assert.Equal("13957b889dde64cc266985175ff3e3840f102f60b13e645e6a43d683abe5e242", slice.ParquetSha256);
        Assert.Equal(61_597, slice.ParquetSizeBytes);
        Assert.Equal(18, slice.Rows);
    }

    [Fact]
    public void AddDatasetSource_OnAServiceCollection_RegistersTheSourceAsAFullSnapshot()
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(new DataPaths(workFolder));
        services.AddDatasetSource();

        using ServiceProvider provider = services.BuildServiceProvider();
        IJobSource source = provider.GetRequiredService<IJobSource>();

        Assert.Equal(JobSourceKind.Dataset, source.Kind);
        Assert.True(source.IsFullSnapshot);
    }

    public void Dispose()
    {
        if (Directory.Exists(workFolder))
        {
            Directory.Delete(workFolder, recursive: true);
        }
    }

    private static async Task<IReadOnlyList<RawJob>> ReadAllAsync(string slicePath)
    {
        DatasetJobReader reader = new();
        List<RawJob> jobs = [];

        await foreach (RawJob job in reader.ReadAsync(slicePath, new DatasetReadFilter(NotBefore, new TitleRulesStub()), CancellationToken.None))
        {
            jobs.Add(job);
        }

        return jobs;
    }

    private async Task<string> WriteSliceAsync()
    {
        Directory.CreateDirectory(workFolder);
        string slicePath = Path.Combine(workFolder, "jobs.parquet");

        FixtureRow[] rows =
        [
            new("https://boards.greenhouse.io/acme/jobs/1", "Senior Backend Engineer", "Acme", "greenhouse", "1001", "Remote, Europe", "DE", "EMEA", null, true, 90_000, 120_000, "EUR", "YEAR", "FULL_TIME", "Engineering", "## About\nWe run **.NET** services.", "2026-09-11T10:23:28.662000+00:00", "https://boards.greenhouse.io/acme/jobs/1/apply"),
            new("https://boards.greenhouse.io/acme/jobs/2", "Platform Engineer", "Acme", "greenhouse", "1002", null, null, null, null, null, null, null, null, null, null, "Infrastructure", "Own the delivery platform.", null, null),
            new("https://boards.greenhouse.io/acme/jobs/3", "Backend Engineer", "Acme", "greenhouse", "1003", null, "US", null, null, true, null, null, null, null, null, null, "Posted before the window.", "2026-07-01T12:41:33.085000+00:00", null),
            new("https://boards.greenhouse.io/acme/jobs/4", "Senior Frontend Engineer", "Acme", "greenhouse", "1004", null, "DE", null, null, true, null, null, null, null, null, null, "Excluded by the title rules.", "2026-09-10T09:00:00.000000+00:00", null),
            new("https://boards.greenhouse.io/acme/jobs/5", "Executive Chef", "Acme", "greenhouse", "1005", null, "FR", null, null, false, null, null, null, null, null, null, "No include term in the title.", "2026-09-10T09:00:00.000000+00:00", null),
            new("https://boards.greenhouse.io/acme/jobs/6", "Backend Engineer", "Acme", "greenhouse", "1006", "Paris", "FR", null, "fr", false, null, null, null, null, null, null, "Written in an excluded language.", "2026-09-09T09:00:00.000000+00:00", null),
            new("https://boards.greenhouse.io/acme/jobs/7", "Backend Engineer", "Acme", "greenhouse", "1007", "Utrecht", "NL", null, "en", false, 8_000, 10_000, "EUR", "MONTHLY", "CONTRACT", "Payments", "Posted in the Netherlands.", "2026-09-08T09:00:00.000000+00:00", null),
            new("https://boards.greenhouse.io/acme/jobs/8", "Distributed Systems Engineer", "Acme", "greenhouse", "1008", "Remote", null, "Worldwide", "en-US", true, null, null, null, null, null, null, "Data plane work.", "2026-09-07T09:00:00.000000+00:00", null),
            new("https://jobs.lever.co/beta/9", "Backend Engineer", "Beta", "lever", "", "Austin, Texas", "", null, "en", false, 70, 90, "USD", "HOUR", "CONTRACT", null, "Hourly contract.", "2026-09-05T00:00:00", "https://jobs.lever.co/beta/9/apply"),
            new("https://boards.greenhouse.io/acme/jobs/10", null, "Acme", "greenhouse", "1010", null, null, null, null, null, null, null, null, null, null, null, "No title at all.", "2026-09-06T09:00:00.000000+00:00", null)
        ];

        DataField url = new DataField<string>("url", nullable: true);
        DataField title = new DataField<string>("title", nullable: true);
        DataField company = new DataField<string>("company", nullable: true);
        DataField atsType = new DataField<string>("ats_type", nullable: true);
        DataField atsId = new DataField<string>("ats_id", nullable: true);
        DataField location = new DataField<string>("location", nullable: true);
        DataField countryIso = new DataField<string>("country_iso", nullable: true);
        DataField region = new DataField<string>("region", nullable: true);
        DataField language = new DataField<string>("language", nullable: true);
        DataField latitude = new DataField<string>("lat", nullable: true);
        DataField longitude = new DataField<string>("lon", nullable: true);
        DataField isRemote = new DataField<bool?>("is_remote");
        DataField salaryMin = new DataField<double?>("salary_min");
        DataField salaryMax = new DataField<double?>("salary_max");
        DataField salaryCurrency = new DataField<string>("salary_currency", nullable: true);
        DataField salaryPeriod = new DataField<string>("salary_period", nullable: true);
        DataField salarySummary = new DataField<string>("salary_summary", nullable: true);
        DataField employmentType = new DataField<string>("employment_type", nullable: true);
        DataField department = new DataField<string>("department", nullable: true);
        DataField team = new DataField<string>("team", nullable: true);
        DataField description = new DataField<string>("description", nullable: true);
        DataField postedAt = new DataField<string>("posted_at", nullable: true);
        DataField requisitionId = new DataField<string>("requisition_id", nullable: true);
        DataField applyUrl = new DataField<string>("apply_url", nullable: true);
        DataField commitment = new DataField<string>("commitment", nullable: true);
        DataField raw = new DataField<string>("raw", nullable: true);

        ParquetSchema schema = new(url, title, company, atsType, atsId, location, countryIso, region, language, latitude, longitude, isRemote, salaryMin, salaryMax, salaryCurrency, salaryPeriod, salarySummary, employmentType, department, team, description, postedAt, requisitionId, applyUrl, commitment, raw);

        await using FileStream stream = File.Create(slicePath);
        await using ParquetWriter writer = await ParquetWriter.CreateAsync(schema, stream);

        foreach (FixtureRow[] chunk in new[] { rows[..5], rows[5..] })
        {
            using ParquetRowGroupWriter group = writer.CreateRowGroup();
            string?[] empty = new string?[chunk.Length];

            await group.WriteAsync(url, chunk.Select(row => row.Url).ToArray());
            await group.WriteAsync(title, chunk.Select(row => row.Title).ToArray());
            await group.WriteAsync(company, chunk.Select(row => row.Company).ToArray());
            await group.WriteAsync(atsType, chunk.Select(row => row.AtsType).ToArray());
            await group.WriteAsync(atsId, chunk.Select(row => row.AtsId).ToArray());
            await group.WriteAsync(location, chunk.Select(row => row.Location).ToArray());
            await group.WriteAsync(countryIso, chunk.Select(row => row.CountryIso).ToArray());
            await group.WriteAsync(region, chunk.Select(row => row.Region).ToArray());
            await group.WriteAsync(language, chunk.Select(row => row.Language).ToArray());
            await group.WriteAsync(latitude, empty);
            await group.WriteAsync(longitude, empty);
            await group.WriteAsync<bool>(isRemote, new ReadOnlyMemory<bool?>(chunk.Select(row => row.IsRemote).ToArray()));
            await group.WriteAsync<double>(salaryMin, new ReadOnlyMemory<double?>(chunk.Select(row => row.SalaryMin).ToArray()));
            await group.WriteAsync<double>(salaryMax, new ReadOnlyMemory<double?>(chunk.Select(row => row.SalaryMax).ToArray()));
            await group.WriteAsync(salaryCurrency, chunk.Select(row => row.SalaryCurrency).ToArray());
            await group.WriteAsync(salaryPeriod, chunk.Select(row => row.SalaryPeriod).ToArray());
            await group.WriteAsync(salarySummary, empty);
            await group.WriteAsync(employmentType, chunk.Select(row => row.EmploymentType).ToArray());
            await group.WriteAsync(department, chunk.Select(row => row.Department).ToArray());
            await group.WriteAsync(team, empty);
            await group.WriteAsync(description, chunk.Select(row => row.Description).ToArray());
            await group.WriteAsync(postedAt, chunk.Select(row => row.PostedAt).ToArray());
            await group.WriteAsync(requisitionId, empty);
            await group.WriteAsync(applyUrl, chunk.Select(row => row.ApplyUrl).ToArray());
            await group.WriteAsync(commitment, empty);
            await group.WriteAsync(raw, empty);
        }

        return slicePath;
    }

    /// <summary>Writes a slice the way a live applicant tracking system varies it: the identifier as a whole number, the pay as text, the remote flag as a flag and every other column as text.</summary>
    private async Task<string> WriteMixedTypeSliceAsync()
    {
        Directory.CreateDirectory(workFolder);
        string slicePath = Path.Combine(workFolder, "mixed-types.parquet");

        DataField url = new DataField<string>("url", nullable: true);
        DataField title = new DataField<string>("title", nullable: true);
        DataField company = new DataField<string>("company", nullable: true);
        DataField atsType = new DataField<string>("ats_type", nullable: true);
        DataField atsId = new DataField<long?>("ats_id");
        DataField location = new DataField<string>("location", nullable: true);
        DataField countryIso = new DataField<string>("country_iso", nullable: true);
        DataField region = new DataField<string>("region", nullable: true);
        DataField language = new DataField<string>("language", nullable: true);
        DataField isRemote = new DataField<bool?>("is_remote");
        DataField salaryMin = new DataField<string>("salary_min", nullable: true);
        DataField salaryMax = new DataField<string>("salary_max", nullable: true);
        DataField salaryCurrency = new DataField<string>("salary_currency", nullable: true);
        DataField salaryPeriod = new DataField<string>("salary_period", nullable: true);
        DataField employmentType = new DataField<string>("employment_type", nullable: true);
        DataField department = new DataField<string>("department", nullable: true);
        DataField description = new DataField<string>("description", nullable: true);
        DataField postedAt = new DataField<string>("posted_at", nullable: true);
        DataField applyUrl = new DataField<string>("apply_url", nullable: true);

        ParquetSchema schema = new(url, title, company, atsType, atsId, location, countryIso, region, language, isRemote, salaryMin, salaryMax, salaryCurrency, salaryPeriod, employmentType, department, description, postedAt, applyUrl);

        await using FileStream stream = File.Create(slicePath);
        await using ParquetWriter writer = await ParquetWriter.CreateAsync(schema, stream);
        using ParquetRowGroupWriter group = writer.CreateRowGroup();

        await group.WriteAsync(url, new[] { "https://boards.greenhouse.io/acme/jobs/1", "https://boards.greenhouse.io/acme/jobs/2" });
        await group.WriteAsync(title, new[] { "Senior Backend Engineer", "Platform Engineer" });
        await group.WriteAsync(company, new[] { "Acme", "Acme" });
        await group.WriteAsync(atsType, new[] { "greenhouse", "greenhouse" });
        await group.WriteAsync<long>(atsId, new ReadOnlyMemory<long?>([1001L, 1002L]));
        await group.WriteAsync(location, new string?[] { "Remote, Europe", null });
        await group.WriteAsync(countryIso, new string?[] { "DE", null });
        await group.WriteAsync(region, new string?[] { "EMEA", null });
        await group.WriteAsync(language, new string?[] { "en", null });
        await group.WriteAsync<bool>(isRemote, new ReadOnlyMemory<bool?>([true, false]));
        await group.WriteAsync(salaryMin, new string?[] { "90000", "competitive" });
        await group.WriteAsync(salaryMax, new string?[] { "120500.75", null });
        await group.WriteAsync(salaryCurrency, new string?[] { "EUR", null });
        await group.WriteAsync(salaryPeriod, new string?[] { "YEAR", null });
        await group.WriteAsync(employmentType, new string?[] { "FULL_TIME", null });
        await group.WriteAsync(department, new string?[] { "Engineering", null });
        await group.WriteAsync(description, new[] { "We run .NET services.", "We run the delivery platform." });
        await group.WriteAsync(postedAt, new[] { "2026-09-11T10:23:28.662000+00:00", "2026-09-05T00:00:00" });
        await group.WriteAsync(applyUrl, new string?[] { "https://boards.greenhouse.io/acme/jobs/1/apply", null });

        return slicePath;
    }

    private sealed record FixtureRow(
        string? Url,
        string? Title,
        string? Company,
        string? AtsType,
        string? AtsId,
        string? Location,
        string? CountryIso,
        string? Region,
        string? Language,
        bool? IsRemote,
        double? SalaryMin,
        double? SalaryMax,
        string? SalaryCurrency,
        string? SalaryPeriod,
        string? EmploymentType,
        string? Department,
        string? Description,
        string? PostedAt,
        string? ApplyUrl);

    private sealed class TitleRulesStub : ITitleRules
    {
        public TitleVerdict Evaluate(string title)
        {
            if (title.Contains("Frontend", StringComparison.OrdinalIgnoreCase))
            {
                return TitleVerdict.ExcludedByRule("frontend");
            }

            return title.Contains("Engineer", StringComparison.OrdinalIgnoreCase) ? TitleVerdict.Included : TitleVerdict.NoIncludeTerm;
        }
    }
}
