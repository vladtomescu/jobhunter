using System.Globalization;
using JobHunter.Data;
using JobHunter.Domain;
using JobHunter.Pipeline;
using JobHunter.Sources;
using JobHunter.Sources.Dataset;
using JobHunter.Tests.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using ParquetSharp;

namespace JobHunter.Tests.Sources;

/// <summary>Proves the dataset reader against a parquet slice written by the test with the column names and types of the live schema.</summary>
public sealed class DatasetJobReaderTests : IDisposable
{
    private static readonly DateTimeOffset NotBefore = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly string workFolder = Path.Combine(Path.GetTempPath(), "jobhunter-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Read_OverASliceOfMixedRows_KeepsOnlyRecentAllowedTitlesInAnAllowedLanguage()
    {
        string slicePath = WriteSlice();

        IReadOnlyList<RawJob> jobs = ReadAll(slicePath);

        Assert.Equal<string>(
            ["greenhouse:1001", "greenhouse:1002", "greenhouse:1007", "greenhouse:1008", "https://jobs.lever.co/beta/9"],
            jobs.Select(job => job.SourceId).ToArray());
    }

    [Fact]
    public void Read_ForACandidateWhoAcceptsEnglishOnly_LeavesOutTheDutchRow()
    {
        string slicePath = WriteSlice();

        IReadOnlyList<RawJob> jobs = ReadAll(slicePath, CandidateProfiles.Berlin);

        Assert.Equal<string>(
            ["greenhouse:1001", "greenhouse:1002", "greenhouse:1008", "https://jobs.lever.co/beta/9"],
            jobs.Select(job => job.SourceId).ToArray());
    }

    [Fact]
    public void Read_ForARowWithEveryColumnSet_MapsEveryColumnTheRequirementsName()
    {
        string slicePath = WriteSlice();

        RawJob job = ReadAll(slicePath).First();

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
    public void Read_ForARowWithoutAPostingDate_KeepsTheRowWithAnUnknownDate()
    {
        string slicePath = WriteSlice();

        RawJob job = ReadAll(slicePath).Single(candidate => candidate.SourceId == "greenhouse:1002");

        Assert.Null(job.PostedAt);
        Assert.Null(job.IsRemote);
        Assert.Null(job.CompMin);
        Assert.Null(job.CompPeriod);
    }

    [Fact]
    public void Read_ForARowWithoutAnApplicantTrackingIdentifier_FallsBackToTheUrlAndMapsHourlyPay()
    {
        string slicePath = WriteSlice();

        RawJob job = ReadAll(slicePath).Single(candidate => candidate.SourceId == "https://jobs.lever.co/beta/9");

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
    public void Read_ForARowWhosePeriodIsSpelledOut_ReadsItThroughTheSharedPeriodParser()
    {
        string slicePath = WriteSlice();

        RawJob job = ReadAll(slicePath).Single(candidate => candidate.SourceId == "greenhouse:1007");

        Assert.Equal(8_000m, job.CompMin);
        Assert.Equal(10_000m, job.CompMax);
        Assert.Equal("EUR", job.CompCurrency);
        Assert.Equal(CompPeriod.Month, job.CompPeriod);
    }

    [Fact]
    public void Read_OverASliceOfSeveralRowGroups_YieldsRowsFromEveryGroup()
    {
        string slicePath = WriteSlice();

        using (ParquetFileReader file = new(slicePath))
        {
            Assert.Equal(2, file.FileMetaData.NumRowGroups);
        }

        IReadOnlyList<RawJob> jobs = ReadAll(slicePath);

        Assert.Equal(2, jobs.Count(job => job.SourceId is "greenhouse:1001" or "greenhouse:1002"));
        Assert.Equal(3, jobs.Count(job => job.SourceId is "greenhouse:1007" or "greenhouse:1008" or "https://jobs.lever.co/beta/9"));
    }

    [Fact]
    public void Read_ForASliceWhoseColumnsCarryOtherTypes_ReadsEachColumnByTheTypeItIsStoredAs()
    {
        string slicePath = WriteMixedTypeSlice();

        IReadOnlyList<RawJob> jobs = ReadAll(slicePath);

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

    private static IReadOnlyList<RawJob> ReadAll(string slicePath, CandidateProfile? candidate = null)
    {
        DatasetJobReader reader = new();

        return [.. reader.Read(slicePath, new DatasetReadFilter(NotBefore, candidate ?? CandidateProfiles.NetherlandsJava), CancellationToken.None)];
    }

    private static void WriteColumn<TValue>(RowGroupWriter group, IEnumerable<TValue> values)
    {
        using LogicalColumnWriter<TValue> column = group.NextColumn().LogicalWriter<TValue>();

        column.WriteBatch(values.ToArray());
    }

    private string WriteSlice()
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
            new("https://boards.greenhouse.io/acme/jobs/7", "Backend Engineer", "Acme", "greenhouse", "1007", "Utrecht", "NL", null, "nl", false, 8_000, 10_000, "EUR", "MONTHLY", "CONTRACT", "Payments", "Dutch posting.", "2026-09-08T09:00:00.000000+00:00", null),
            new("https://boards.greenhouse.io/acme/jobs/8", "Distributed Systems Engineer", "Acme", "greenhouse", "1008", "Remote", null, "Worldwide", "en-US", true, null, null, null, null, null, null, "Data plane work.", "2026-09-07T09:00:00.000000+00:00", null),
            new("https://jobs.lever.co/beta/9", "Backend Engineer", "Beta", "lever", "", "Austin, Texas", "", null, "en", false, 70, 90, "USD", "HOUR", "CONTRACT", null, "Hourly contract.", "2026-09-05T00:00:00", "https://jobs.lever.co/beta/9/apply"),
            new("https://boards.greenhouse.io/acme/jobs/10", null, "Acme", "greenhouse", "1010", null, null, null, null, null, null, null, null, null, null, null, "No title at all.", "2026-09-06T09:00:00.000000+00:00", null)
        ];

        Column[] columns =
        [
            new Column<string?>("url"),
            new Column<string?>("title"),
            new Column<string?>("company"),
            new Column<string?>("ats_type"),
            new Column<string?>("ats_id"),
            new Column<string?>("location"),
            new Column<string?>("country_iso"),
            new Column<string?>("region"),
            new Column<string?>("language"),
            new Column<string?>("lat"),
            new Column<string?>("lon"),
            new Column<bool?>("is_remote"),
            new Column<double?>("salary_min"),
            new Column<double?>("salary_max"),
            new Column<string?>("salary_currency"),
            new Column<string?>("salary_period"),
            new Column<string?>("salary_summary"),
            new Column<string?>("employment_type"),
            new Column<string?>("department"),
            new Column<string?>("team"),
            new Column<string?>("description"),
            new Column<string?>("posted_at"),
            new Column<string?>("requisition_id"),
            new Column<string?>("apply_url"),
            new Column<string?>("commitment"),
            new Column<string?>("raw")
        ];

        using ParquetFileWriter writer = new(slicePath, columns);

        foreach (FixtureRow[] chunk in new[] { rows[..5], rows[5..] })
        {
            using RowGroupWriter group = writer.AppendRowGroup();
            string?[] empty = new string?[chunk.Length];

            WriteColumn(group, chunk.Select(row => row.Url));
            WriteColumn(group, chunk.Select(row => row.Title));
            WriteColumn(group, chunk.Select(row => row.Company));
            WriteColumn(group, chunk.Select(row => row.AtsType));
            WriteColumn(group, chunk.Select(row => row.AtsId));
            WriteColumn(group, chunk.Select(row => row.Location));
            WriteColumn(group, chunk.Select(row => row.CountryIso));
            WriteColumn(group, chunk.Select(row => row.Region));
            WriteColumn(group, chunk.Select(row => row.Language));
            WriteColumn(group, empty);
            WriteColumn(group, empty);
            WriteColumn(group, chunk.Select(row => row.IsRemote));
            WriteColumn(group, chunk.Select(row => row.SalaryMin));
            WriteColumn(group, chunk.Select(row => row.SalaryMax));
            WriteColumn(group, chunk.Select(row => row.SalaryCurrency));
            WriteColumn(group, chunk.Select(row => row.SalaryPeriod));
            WriteColumn(group, empty);
            WriteColumn(group, chunk.Select(row => row.EmploymentType));
            WriteColumn(group, chunk.Select(row => row.Department));
            WriteColumn(group, empty);
            WriteColumn(group, chunk.Select(row => row.Description));
            WriteColumn(group, chunk.Select(row => row.PostedAt));
            WriteColumn(group, empty);
            WriteColumn(group, chunk.Select(row => row.ApplyUrl));
            WriteColumn(group, empty);
            WriteColumn(group, empty);
        }

        writer.Close();

        return slicePath;
    }

    /// <summary>Writes a slice the way a live applicant tracking system varies it: the identifier as a whole number, the pay as text, the remote flag as a flag and every other column as text.</summary>
    private string WriteMixedTypeSlice()
    {
        Directory.CreateDirectory(workFolder);
        string slicePath = Path.Combine(workFolder, "mixed-types.parquet");

        Column[] columns =
        [
            new Column<string?>("url"),
            new Column<string?>("title"),
            new Column<string?>("company"),
            new Column<string?>("ats_type"),
            new Column<long?>("ats_id"),
            new Column<string?>("location"),
            new Column<string?>("country_iso"),
            new Column<string?>("region"),
            new Column<string?>("language"),
            new Column<bool?>("is_remote"),
            new Column<string?>("salary_min"),
            new Column<string?>("salary_max"),
            new Column<string?>("salary_currency"),
            new Column<string?>("salary_period"),
            new Column<string?>("employment_type"),
            new Column<string?>("department"),
            new Column<string?>("description"),
            new Column<string?>("posted_at"),
            new Column<string?>("apply_url")
        ];

        using ParquetFileWriter writer = new(slicePath, columns);
        using RowGroupWriter group = writer.AppendRowGroup();

        WriteColumn<string?>(group, ["https://boards.greenhouse.io/acme/jobs/1", "https://boards.greenhouse.io/acme/jobs/2"]);
        WriteColumn<string?>(group, ["Senior Backend Engineer", "Platform Engineer"]);
        WriteColumn<string?>(group, ["Acme", "Acme"]);
        WriteColumn<string?>(group, ["greenhouse", "greenhouse"]);
        WriteColumn<long?>(group, [1001L, 1002L]);
        WriteColumn<string?>(group, ["Remote, Europe", null]);
        WriteColumn<string?>(group, ["DE", null]);
        WriteColumn<string?>(group, ["EMEA", null]);
        WriteColumn<string?>(group, ["en", null]);
        WriteColumn<bool?>(group, [true, false]);
        WriteColumn<string?>(group, ["90000", "competitive"]);
        WriteColumn<string?>(group, ["120500.75", null]);
        WriteColumn<string?>(group, ["EUR", null]);
        WriteColumn<string?>(group, ["YEAR", null]);
        WriteColumn<string?>(group, ["FULL_TIME", null]);
        WriteColumn<string?>(group, ["Engineering", null]);
        WriteColumn<string?>(group, ["We run .NET services.", "We run the delivery platform."]);
        WriteColumn<string?>(group, ["2026-09-11T10:23:28.662000+00:00", "2026-09-05T00:00:00"]);
        WriteColumn<string?>(group, ["https://boards.greenhouse.io/acme/jobs/1/apply", null]);

        writer.Close();

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
}
