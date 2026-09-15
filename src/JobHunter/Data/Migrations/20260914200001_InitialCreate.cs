using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Applications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    StatusChangedAt = table.Column<string>(type: "TEXT", nullable: false),
                    AppliedAt = table.Column<string>(type: "TEXT", nullable: true),
                    Channel = table.Column<string>(type: "TEXT", nullable: true),
                    CvVersion = table.Column<string>(type: "TEXT", nullable: true),
                    NextAction = table.Column<string>(type: "TEXT", nullable: true),
                    NextActionDue = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CompDiscussed = table.Column<string>(type: "TEXT", nullable: true),
                    KitState = table.Column<string>(type: "TEXT", nullable: false),
                    KitError = table.Column<string>(type: "TEXT", nullable: true),
                    Contact = table.Column<string>(type: "TEXT", nullable: true),
                    History = table.Column<string>(type: "TEXT", nullable: true),
                    Kit = table.Column<string>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Applications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FetchRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<string>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<string>(type: "TEXT", nullable: true),
                    Trigger = table.Column<string>(type: "TEXT", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", nullable: false),
                    Scored = table.Column<int>(type: "INTEGER", nullable: false),
                    ScoreFailures = table.Column<int>(type: "INTEGER", nullable: false),
                    MarkedInactive = table.Column<int>(type: "INTEGER", nullable: false),
                    MarkedStale = table.Column<int>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    SourceResults = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FetchRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    CanonicalApplyUrl = table.Column<string>(type: "TEXT", nullable: false),
                    ApplyUrl = table.Column<string>(type: "TEXT", nullable: true),
                    PostingUrl = table.Column<string>(type: "TEXT", nullable: false),
                    Company = table.Column<string>(type: "TEXT", nullable: false),
                    CompanyUrl = table.Column<string>(type: "TEXT", nullable: true),
                    Ats = table.Column<string>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    DescriptionText = table.Column<string>(type: "TEXT", nullable: false),
                    DescriptionHash = table.Column<string>(type: "TEXT", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    LocationText = table.Column<string>(type: "TEXT", nullable: true),
                    CountryIso = table.Column<string>(type: "TEXT", nullable: true),
                    RegionText = table.Column<string>(type: "TEXT", nullable: true),
                    IsRemoteFromSource = table.Column<bool>(type: "INTEGER", nullable: true),
                    Language = table.Column<string>(type: "TEXT", nullable: true),
                    EmploymentTypeFromSource = table.Column<string>(type: "TEXT", nullable: true),
                    CompMin = table.Column<double>(type: "REAL", nullable: true),
                    CompMax = table.Column<double>(type: "REAL", nullable: true),
                    CompCurrency = table.Column<string>(type: "TEXT", nullable: true),
                    CompPeriod = table.Column<string>(type: "TEXT", nullable: true),
                    CompMinEurYear = table.Column<double>(type: "REAL", nullable: true),
                    CompMaxEurYear = table.Column<double>(type: "REAL", nullable: true),
                    PostedAt = table.Column<string>(type: "TEXT", nullable: true),
                    FirstSeenAt = table.Column<string>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    MissedRuns = table.Column<int>(type: "INTEGER", nullable: false),
                    IsManual = table.Column<bool>(type: "INTEGER", nullable: false),
                    Prefilter = table.Column<string>(type: "TEXT", nullable: false),
                    DropReason = table.Column<string>(type: "TEXT", nullable: true),
                    Flags = table.Column<string>(type: "TEXT", nullable: false),
                    Scoring = table.Column<string>(type: "TEXT", nullable: false),
                    ScoreError = table.Column<string>(type: "TEXT", nullable: true),
                    Class = table.Column<string>(type: "TEXT", nullable: true),
                    Triage = table.Column<string>(type: "TEXT", nullable: false),
                    TriagedAt = table.Column<string>(type: "TEXT", nullable: true),
                    Score = table.Column<string>(type: "TEXT", nullable: true),
                    Sources = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstName = table.Column<string>(type: "TEXT", nullable: false),
                    LastName = table.Column<string>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    Phone = table.Column<string>(type: "TEXT", nullable: false),
                    Location = table.Column<string>(type: "TEXT", nullable: false),
                    LinkedInUrl = table.Column<string>(type: "TEXT", nullable: false),
                    ResumePdfPath = table.Column<string>(type: "TEXT", nullable: false),
                    ResumeMarkdownPath = table.Column<string>(type: "TEXT", nullable: false),
                    MinB2bHourlyEur = table.Column<double>(type: "REAL", nullable: true),
                    MinEmploymentAnnualEur = table.Column<double>(type: "REAL", nullable: true),
                    TargetAnnualEur = table.Column<double>(type: "REAL", nullable: true),
                    ScoreModel = table.Column<string>(type: "TEXT", nullable: false),
                    KitModel = table.Column<string>(type: "TEXT", nullable: false),
                    RemoteOkEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    WwrEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DatasetEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DatasetAtsList = table.Column<string>(type: "TEXT", nullable: false),
                    FirstRunWindowDays = table.Column<int>(type: "INTEGER", nullable: false),
                    GhostThresholdDays = table.Column<int>(type: "INTEGER", nullable: false),
                    AutoRefreshAfterHours = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxScoresPerRun = table.Column<int>(type: "INTEGER", nullable: false),
                    KeepUsOnlyRemote = table.Column<bool>(type: "INTEGER", nullable: false),
                    KeepOnsiteWithCompOrRelocation = table.Column<bool>(type: "INTEGER", nullable: false),
                    FxOverridesJson = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Applications_JobId",
                table: "Applications",
                column: "JobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FetchRuns_StartedAt",
                table: "FetchRuns",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_Fingerprint",
                table: "Jobs",
                column: "Fingerprint",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Applications");

            migrationBuilder.DropTable(
                name: "FetchRuns");

            migrationBuilder.DropTable(
                name: "Jobs");

            migrationBuilder.DropTable(
                name: "Settings");
        }
    }
}
