using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Data.Migrations
{
    /// <summary>Keeps the stack the model gave on the score card, next to the stack the CV check recomputed. The card is a JSON column, so no table changes: a stored card reads with no model stack until its job is scored again.</summary>
    public partial class KeepModelStack : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
