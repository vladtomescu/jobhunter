using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Data.Migrations
{
    /// <summary>Adds the candidate's home city and renames the home-country flag inside the stored flag lists to the home-city flag.</summary>
    public partial class RecognizeHomeCity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HomeCity",
                table: "Settings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""UPDATE Jobs SET Flags = replace(Flags, '"HomeCountry"', '"HomeCity"');""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE Jobs SET Flags = replace(Flags, '"HomeCity"', '"HomeCountry"');""");

            migrationBuilder.DropColumn(
                name: "HomeCity",
                table: "Settings");
        }
    }
}
