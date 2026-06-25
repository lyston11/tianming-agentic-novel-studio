using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddChapterBlueprints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chapter_blueprints",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    volume_id = table.Column<string>(type: "TEXT", nullable: true),
                    chapter_id = table.Column<string>(type: "TEXT", nullable: false),
                    chapter_index = table.Column<int>(type: "INTEGER", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    intent = table.Column<string>(type: "TEXT", nullable: false),
                    key_events_json = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    characters_json = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    conflict_note = table.Column<string>(type: "TEXT", nullable: true),
                    ending_note = table.Column<string>(type: "TEXT", nullable: true),
                    required_knowledge_ids_json = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    applied_design_rule_ids_json = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    dependency_chapter_ids_json = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    context_package_id = table.Column<string>(type: "TEXT", nullable: true),
                    version = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "Draft"),
                    target_word_count = table.Column<int>(type: "INTEGER", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    previous_version_id = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chapter_blueprints", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chapter_blueprints_project_chapter_status",
                table: "chapter_blueprints",
                columns: new[] { "project_id", "chapter_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_chapter_blueprints_user_project_created",
                table: "chapter_blueprints",
                columns: new[] { "user_id", "project_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_chapter_blueprints_version",
                table: "chapter_blueprints",
                columns: new[] { "project_id", "chapter_id", "version" });

            migrationBuilder.CreateIndex(
                name: "ix_chapter_blueprints_chapter_index",
                table: "chapter_blueprints",
                columns: new[] { "project_id", "chapter_index" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chapter_blueprints");
        }
    }
}
