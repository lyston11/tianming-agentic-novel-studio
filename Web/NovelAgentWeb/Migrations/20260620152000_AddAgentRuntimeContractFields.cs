using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(NovelAgentDbContext))]
    [Migration("20260620152000_AddAgentRuntimeContractFields")]
    public partial class AddAgentRuntimeContractFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "mode",
                table: "agent_runtime_runs",
                type: "TEXT",
                maxLength: 30,
                nullable: false,
                defaultValue: "inspect");

            migrationBuilder.AddColumn<string>(
                name: "source_message_id",
                table: "agent_runtime_runs",
                type: "TEXT",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "agent_runtime_runs",
                type: "TEXT",
                maxLength: 160,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "budget_json",
                table: "agent_runtime_runs",
                type: "TEXT",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "failure_json",
                table: "agent_runtime_runs",
                type: "TEXT",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "stage",
                table: "agent_runtime_events",
                type: "TEXT",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "agent_runtime_events",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "artifact_type",
                table: "agent_runtime_events",
                type: "TEXT",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "artifact_id",
                table: "agent_runtime_events",
                type: "TEXT",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "display_surface",
                table: "agent_runtime_events",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "chat");

            migrationBuilder.AddColumn<string>(
                name: "display_policy",
                table: "agent_runtime_events",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "collapsible");

            migrationBuilder.CreateIndex(
                name: "idx_agent_runtime_runs_idempotency",
                table: "agent_runtime_runs",
                columns: new[] { "user_id", "session_id", "idempotency_key" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_agent_runtime_runs_idempotency",
                table: "agent_runtime_runs");

            migrationBuilder.DropColumn(name: "mode", table: "agent_runtime_runs");
            migrationBuilder.DropColumn(name: "source_message_id", table: "agent_runtime_runs");
            migrationBuilder.DropColumn(name: "idempotency_key", table: "agent_runtime_runs");
            migrationBuilder.DropColumn(name: "budget_json", table: "agent_runtime_runs");
            migrationBuilder.DropColumn(name: "failure_json", table: "agent_runtime_runs");
            migrationBuilder.DropColumn(name: "stage", table: "agent_runtime_events");
            migrationBuilder.DropColumn(name: "status", table: "agent_runtime_events");
            migrationBuilder.DropColumn(name: "artifact_type", table: "agent_runtime_events");
            migrationBuilder.DropColumn(name: "artifact_id", table: "agent_runtime_events");
            migrationBuilder.DropColumn(name: "display_surface", table: "agent_runtime_events");
            migrationBuilder.DropColumn(name: "display_policy", table: "agent_runtime_events");
        }
    }
}
