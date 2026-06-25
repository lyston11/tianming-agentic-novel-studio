using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(NovelAgentDbContext))]
    [Migration("20260623123000_AddChapterChanges")]
    public partial class AddChapterChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chapter_changes",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: false),
                    runtime_run_id = table.Column<string>(type: "TEXT", nullable: false),
                    chapter_id = table.Column<string>(type: "TEXT", nullable: false),
                    package_id = table.Column<string>(type: "TEXT", nullable: true),
                    changes_json = table.Column<string>(type: "TEXT", nullable: false),
                    canonical_changes_json = table.Column<string>(type: "TEXT", nullable: false),
                    parse_status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "unknown"),
                    parse_error = table.Column<string>(type: "TEXT", nullable: true),
                    applied_to_fact_snapshot = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    applied_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chapter_changes", x => x.id);
                    table.ForeignKey(
                        name: "FK_chapter_changes_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_chapter_changes_project_chapter_created",
                table: "chapter_changes",
                columns: new[] { "project_id", "chapter_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_chapter_changes_project_parse_status",
                table: "chapter_changes",
                columns: new[] { "project_id", "parse_status" });

            migrationBuilder.CreateIndex(
                name: "idx_chapter_changes_run_created",
                table: "chapter_changes",
                columns: new[] { "runtime_run_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "chapter_changes");
        }
    }
}
