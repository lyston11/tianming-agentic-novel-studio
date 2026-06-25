using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    [Migration("20260623124500_AddChapterDrafts")]
    public partial class AddChapterDrafts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chapter_drafts",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: false),
                    runtime_run_id = table.Column<string>(type: "TEXT", nullable: false),
                    chapter_id = table.Column<string>(type: "TEXT", nullable: false),
                    package_id = table.Column<string>(type: "TEXT", nullable: true),
                    artifact_id = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "draft_generated"),
                    draft_content = table.Column<string>(type: "TEXT", nullable: false),
                    changes_json = table.Column<string>(type: "TEXT", nullable: true),
                    content_length = table.Column<int>(type: "INTEGER", nullable: false),
                    repair_attempt_count = table.Column<int>(type: "INTEGER", nullable: false),
                    has_changes = table.Column<bool>(type: "INTEGER", nullable: false),
                    generated_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chapter_drafts", x => x.id);
                    table.ForeignKey(
                        name: "FK_chapter_drafts_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_chapter_drafts_project_chapter_created",
                table: "chapter_drafts",
                columns: new[] { "project_id", "chapter_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_chapter_drafts_project_status",
                table: "chapter_drafts",
                columns: new[] { "project_id", "status" });

            migrationBuilder.CreateIndex(
                name: "idx_chapter_drafts_run_created",
                table: "chapter_drafts",
                columns: new[] { "runtime_run_id", "created_at" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "chapter_drafts");
        }
    }
}
