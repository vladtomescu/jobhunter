using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Data.Migrations
{
    /// <summary>Adds the candidate settings, drops the currency from the comp column names, and gives an existing settings row the neutral defaults a new install gets.</summary>
    public partial class GenericCandidateSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TargetAnnualEur",
                table: "Settings",
                newName: "TargetAnnual");

            migrationBuilder.RenameColumn(
                name: "MinEmploymentAnnualEur",
                table: "Settings",
                newName: "MinEmploymentAnnual");

            migrationBuilder.RenameColumn(
                name: "MinB2bHourlyEur",
                table: "Settings",
                newName: "MinContractorHourly");

            migrationBuilder.RenameColumn(
                name: "KeepUsOnlyRemote",
                table: "Settings",
                newName: "AcceptUnitedStatesRemote");

            migrationBuilder.RenameColumn(
                name: "CompMinEurYear",
                table: "Jobs",
                newName: "CompMinPerYear");

            migrationBuilder.RenameColumn(
                name: "CompMaxEurYear",
                table: "Jobs",
                newName: "CompMaxPerYear");

            migrationBuilder.AddColumn<bool>(
                name: "AcceptEuropeRemote",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AcceptedLanguages",
                table: "Settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BaseCurrency",
                table: "Settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CompComputedInCurrency",
                table: "Settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ContractPreference",
                table: "Settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "HasUnitedStatesWorkAuthorization",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "HighPayThresholdPerYear",
                table: "Settings",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HomeCountryIso",
                table: "Settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StackKeywords",
                table: "Settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TitleExcludeTerms",
                table: "Settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TitleIncludeTerms",
                table: "Settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            string titleIncludeTerms = Domain.Settings.DefaultTitleIncludeTerms.Replace("'", "''", StringComparison.Ordinal);
            string titleExcludeTerms = Domain.Settings.DefaultTitleExcludeTerms.Replace("'", "''", StringComparison.Ordinal);

            migrationBuilder.Sql(
                "UPDATE Settings SET "
                + "AcceptEuropeRemote = 1, "
                + $"AcceptedLanguages = '{Domain.Settings.DefaultAcceptedLanguages}', "
                + $"BaseCurrency = '{Domain.Settings.DefaultBaseCurrency}', "
                + $"CompComputedInCurrency = '{Domain.Settings.DefaultBaseCurrency}', "
                + $"ContractPreference = '{nameof(Domain.ContractPreference.Either)}', "
                + $"TitleIncludeTerms = '{titleIncludeTerms}', "
                + $"TitleExcludeTerms = '{titleExcludeTerms}';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcceptEuropeRemote",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "AcceptedLanguages",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "BaseCurrency",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "CompComputedInCurrency",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ContractPreference",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "HasUnitedStatesWorkAuthorization",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "HighPayThresholdPerYear",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "HomeCountryIso",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "StackKeywords",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "TitleExcludeTerms",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "TitleIncludeTerms",
                table: "Settings");

            migrationBuilder.RenameColumn(
                name: "TargetAnnual",
                table: "Settings",
                newName: "TargetAnnualEur");

            migrationBuilder.RenameColumn(
                name: "MinEmploymentAnnual",
                table: "Settings",
                newName: "MinEmploymentAnnualEur");

            migrationBuilder.RenameColumn(
                name: "MinContractorHourly",
                table: "Settings",
                newName: "MinB2bHourlyEur");

            migrationBuilder.RenameColumn(
                name: "AcceptUnitedStatesRemote",
                table: "Settings",
                newName: "KeepUsOnlyRemote");

            migrationBuilder.RenameColumn(
                name: "CompMinPerYear",
                table: "Jobs",
                newName: "CompMinEurYear");

            migrationBuilder.RenameColumn(
                name: "CompMaxPerYear",
                table: "Jobs",
                newName: "CompMaxEurYear");
        }
    }
}
