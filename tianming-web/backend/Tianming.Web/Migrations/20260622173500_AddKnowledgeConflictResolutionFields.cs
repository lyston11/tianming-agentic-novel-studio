using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(NovelAgentDbContext))]
    [Migration("20260622173500_AddKnowledgeConflictResolutionFields")]
    public partial class AddKnowledgeConflictResolutionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "resolved_at",
                table: "knowledge_conflict_reports",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resolution_note",
                table: "knowledge_conflict_reports",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resolved_by_session_id",
                table: "knowledge_conflict_reports",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resolved_by_run_id",
                table: "knowledge_conflict_reports",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "resolved_at",
                table: "knowledge_conflict_reports");

            migrationBuilder.DropColumn(
                name: "resolution_note",
                table: "knowledge_conflict_reports");

            migrationBuilder.DropColumn(
                name: "resolved_by_session_id",
                table: "knowledge_conflict_reports");

            migrationBuilder.DropColumn(
                name: "resolved_by_run_id",
                table: "knowledge_conflict_reports");
        }
    }
}
