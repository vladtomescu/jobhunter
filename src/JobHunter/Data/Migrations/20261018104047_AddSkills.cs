using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunter.Data.Migrations
{
    /// <summary>Adds the skill catalog, seeded with a few common skills and the aliases postings write them under, none of them judged against a CV yet, and gives every stored score card an empty skills list, marked as scored before skills; the skills of each job stay inside its score card.</summary>
    public partial class AddSkills : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Skills",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    Aliases = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    CvMark = table.Column<string>(type: "TEXT", nullable: true),
                    CvMarkSetBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Skills", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Skills_Name",
                table: "Skills",
                column: "Name",
                unique: true);

            migrationBuilder.Sql(
                """
                INSERT INTO Skills (Name, Aliases, Category, CvMark, CvMarkSetBy) VALUES
                    ('.NET', '[".net","dotnet",".net core","asp.net core","c#"]', 'Framework', NULL, NULL),
                    ('AWS', '["aws","amazon web services"]', 'Cloud', NULL, NULL),
                    ('Go', '["go","golang"]', 'Language', NULL, NULL),
                    ('Kubernetes', '["kubernetes","k8s"]', 'Tool', NULL, NULL),
                    ('PostgreSQL', '["postgresql","postgres"]', 'Database', NULL, NULL),
                    ('JavaScript', '["javascript","js"]', 'Language', NULL, NULL),
                    ('TypeScript', '["typescript","ts"]', 'Language', NULL, NULL);
                """);

            // A stored card without the skills list would read its skills as null, so every card scored before skills gets an empty list, marked so that the backfill can tell it from a card whose posting names no skill.
            migrationBuilder.Sql("""UPDATE Jobs SET Score = json_set(Score, '$.Skills', json('[]'), '$.ScoredBeforeSkills', json('true')) WHERE Score IS NOT NULL AND json_type(Score, '$.Skills') IS NOT 'array';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE Jobs SET Score = json_remove(Score, '$.Skills', '$.ScoredBeforeSkills') WHERE Score IS NOT NULL;""");

            migrationBuilder.DropTable(
                name: "Skills");
        }
    }
}
