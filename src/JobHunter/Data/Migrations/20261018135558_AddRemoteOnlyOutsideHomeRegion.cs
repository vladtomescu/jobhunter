using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Data.Migrations
{
    /// <summary>Adds the setting that holds a job located outside the candidate's home region at C unless it is remote and open to the candidate's region; it starts off, for an existing settings row as for a new install, and the candidate turns it on in the settings.</summary>
    public partial class AddRemoteOnlyOutsideHomeRegion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RemoteOnlyOutsideHomeRegion",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RemoteOnlyOutsideHomeRegion",
                table: "Settings");
        }
    }
}
