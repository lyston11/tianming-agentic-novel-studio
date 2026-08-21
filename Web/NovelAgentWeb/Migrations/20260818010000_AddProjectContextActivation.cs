using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations;

[DbContext(typeof(NovelAgentDbContext))]
[Migration("20260818010000_AddProjectContextActivation")]
public sealed class AddProjectContextActivation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "binding_version",
            table: "agent_sessions",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.CreateTable(
            name: "project_context_activations",
            columns: table => new
            {
                id = table.Column<string>(type: "TEXT", nullable: false),
                user_id = table.Column<string>(type: "TEXT", nullable: false),
                session_id = table.Column<string>(type: "TEXT", nullable: false),
                project_id = table.Column<string>(type: "TEXT", nullable: false),
                source_user_message_id = table.Column<string>(type: "TEXT", nullable: true),
                confirmation_action_id = table.Column<string>(type: "TEXT", nullable: true),
                idempotency_key = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                previous_binding_version = table.Column<long>(type: "INTEGER", nullable: false),
                binding_version = table.Column<long>(type: "INTEGER", nullable: false),
                confirmed_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_project_context_activations", x => x.id);
                table.ForeignKey(
                    name: "FK_project_context_activations_agent_sessions_session_id",
                    column: x => x.session_id,
                    principalTable: "agent_sessions",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "idx_project_context_activations_idempotency",
            table: "project_context_activations",
            columns: new[] { "session_id", "idempotency_key" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "idx_project_context_activations_version",
            table: "project_context_activations",
            columns: new[] { "session_id", "binding_version" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "project_context_activations");
        if (ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            migrationBuilder.Sql("ALTER TABLE agent_sessions DROP COLUMN binding_version;");
            return;
        }

        migrationBuilder.DropColumn(name: "binding_version", table: "agent_sessions");
    }
}
