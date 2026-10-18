using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Data.Migrations
{
    /// <summary>Adds the setting that lists the companies whose jobs are never stored; it starts empty, for an existing settings row as for a new install.</summary>
    public partial class AddExcludedCompanies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExcludedCompanies",
                table: "Settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExcludedCompanies",
                table: "Settings");
        }
    }
}
