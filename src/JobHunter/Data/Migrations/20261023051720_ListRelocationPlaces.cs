using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Data.Migrations
{
    /// <summary>Replaces the accepted remote regions and the two geography switches with the list of places the candidate would move to; the list starts empty, and going back restores the switches at the defaults a new install had.</summary>
    public partial class ListRelocationPlaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcceptEuropeRemote",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "AcceptUnitedStatesRemote",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "KeepOnsiteWithCompOrRelocation",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "RemoteOnlyOutsideHomeRegion",
                table: "Settings");

            migrationBuilder.AddColumn<string>(
                name: "RelocationPlaces",
                table: "Settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RelocationPlaces",
                table: "Settings");

            migrationBuilder.AddColumn<bool>(
                name: "AcceptEuropeRemote",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "AcceptUnitedStatesRemote",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "KeepOnsiteWithCompOrRelocation",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "RemoteOnlyOutsideHomeRegion",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }
    }
}
