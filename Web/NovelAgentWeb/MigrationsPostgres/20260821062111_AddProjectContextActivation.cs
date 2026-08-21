using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.MigrationsPostgres
{
    /// <inheritdoc />
    public partial class AddProjectContextActivation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "binding_version",
                table: "agent_sessions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "project_context_activations",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    session_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    source_user_message_id = table.Column<string>(type: "text", nullable: true),
                    confirmation_action_id = table.Column<string>(type: "text", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    previous_binding_version = table.Column<long>(type: "bigint", nullable: false),
                    binding_version = table.Column<long>(type: "bigint", nullable: false),
                    confirmed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_context_activations");

            migrationBuilder.DropColumn(
                name: "binding_version",
                table: "agent_sessions");
        }
    }
}
