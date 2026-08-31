using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(NovelAgentDbContext))]
    [Migration("20260620175000_AddCreativeIntents")]
    public partial class AddCreativeIntents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "creative_intents",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: false),
                    session_id = table.Column<string>(type: "TEXT", nullable: true),
                    runtime_run_id = table.Column<string>(type: "TEXT", nullable: true),
                    idempotency_key = table.Column<string>(type: "TEXT", nullable: true),
                    source = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "chat"),
                    raw_content = table.Column<string>(type: "TEXT", nullable: false),
                    normalized_intent = table.Column<string>(type: "TEXT", nullable: false),
                    target_scope = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "project"),
                    target_volume_id = table.Column<string>(type: "TEXT", nullable: true),
                    target_chapter_id = table.Column<string>(type: "TEXT", nullable: true),
                    target_character_name = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "candidate"),
                    impact_level = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "future_carry"),
                    requires_confirmation = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    conflict_status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "unknown"),
                    decision_reason = table.Column<string>(type: "TEXT", nullable: true),
                    metadata_json = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    decided_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    executed_at = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_creative_intents", x => x.id);
                    table.ForeignKey(
                        name: "FK_creative_intents_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_creative_intents_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_creative_intents_project_chapter_status",
                table: "creative_intents",
                columns: new[] { "project_id", "target_chapter_id", "status" });

            migrationBuilder.CreateIndex(
                name: "idx_creative_intents_project_status_created",
                table: "creative_intents",
                columns: new[] { "project_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_creative_intents_session_created",
                table: "creative_intents",
                columns: new[] { "session_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_creative_intents_idempotency",
                table: "creative_intents",
                columns: new[] { "user_id", "project_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_creative_intents_user_id",
                table: "creative_intents",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "creative_intents");
        }
    }
}
