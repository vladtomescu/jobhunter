using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Data.Migrations
{
    /// <summary>Names the interview statuses after the rounds: Interview1 and Interview2 become Tech and Final becomes Fit, in the status column and inside the stored status history, which names a status in every entry and could not be read with an unknown one.</summary>
    public partial class NameStatusesAfterInterviewRounds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE Applications SET Status = 'Tech' WHERE Status IN ('Interview1', 'Interview2');""");
            migrationBuilder.Sql("""UPDATE Applications SET Status = 'Fit' WHERE Status = 'Final';""");
            migrationBuilder.Sql("""UPDATE Applications SET History = replace(replace(replace(History, '"Status":"Interview1"', '"Status":"Tech"'), '"Status":"Interview2"', '"Status":"Tech"'), '"Status":"Final"', '"Status":"Fit"');""");
        }

        /// <inheritdoc />
        /// <remarks>Lossy: Manager and Tech both go back to Interview1, so an application that was at Interview2 before the rename comes back at Interview1, and SystemDesign goes to Interview2 and Fit to Final.</remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE Applications SET Status = 'Interview1' WHERE Status IN ('Manager', 'Tech');""");
            migrationBuilder.Sql("""UPDATE Applications SET Status = 'Interview2' WHERE Status = 'SystemDesign';""");
            migrationBuilder.Sql("""UPDATE Applications SET Status = 'Final' WHERE Status = 'Fit';""");
            migrationBuilder.Sql("""UPDATE Applications SET History = replace(replace(replace(replace(History, '"Status":"Manager"', '"Status":"Interview1"'), '"Status":"Tech"', '"Status":"Interview1"'), '"Status":"SystemDesign"', '"Status":"Interview2"'), '"Status":"Fit"', '"Status":"Final"');""");
        }
    }
}
