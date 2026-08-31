using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    [Migration("20260623131500_AddMemoryReadsAndPromotions")]
    public partial class AddMemoryReadsAndPromotions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_memory_reads",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: true),
                    session_id = table.Column<string>(type: "TEXT", nullable: true),
                    run_id = table.Column<string>(type: "TEXT", nullable: true),
                    memory_scope = table.Column<string>(type: "TEXT", nullable: false),
                    memory_keys_json = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]"),
                    source_type = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "memory_repository"),
                    consumer = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_memory_reads", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_memory_reads_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_memory_reads_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_memory_promotions",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: true),
                    session_id = table.Column<string>(type: "TEXT", nullable: true),
                    run_id = table.Column<string>(type: "TEXT", nullable: true),
                    source_scope = table.Column<string>(type: "TEXT", nullable: false),
                    target_scope = table.Column<string>(type: "TEXT", nullable: false),
                    source_memory_key = table.Column<string>(type: "TEXT", nullable: false),
                    target_memory_key = table.Column<string>(type: "TEXT", nullable: false),
                    promotion_reason = table.Column<string>(type: "TEXT", nullable: false),
                    payload_json = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_memory_promotions", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_memory_promotions_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_memory_promotions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_agent_memory_reads_project_created",
                table: "agent_memory_reads",
                columns: new[] { "project_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_agent_memory_reads_run",
                table: "agent_memory_reads",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "idx_agent_memory_reads_session",
                table: "agent_memory_reads",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "idx_agent_memory_reads_user_created",
                table: "agent_memory_reads",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_agent_memory_promotions_project_created",
                table: "agent_memory_promotions",
                columns: new[] { "project_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "idx_agent_memory_promotions_run",
                table: "agent_memory_promotions",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "idx_agent_memory_promotions_session",
                table: "agent_memory_promotions",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "idx_agent_memory_promotions_user_created",
                table: "agent_memory_promotions",
                columns: new[] { "user_id", "created_at" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "agent_memory_promotions");
            migrationBuilder.DropTable(name: "agent_memory_reads");
        }
    }
}
