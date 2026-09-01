using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations;

[DbContext(typeof(NovelAgentDbContext))]
[Migration("20260802070000_UnifyAgentProductionArchitecture")]
public sealed class UnifyAgentProductionArchitecture : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "agent_chat_request_receipts",
            columns: table => new
            {
                id = table.Column<string>(nullable: false),
                user_id = table.Column<string>(nullable: false),
                requested_session_id = table.Column<string>(maxLength: 160, nullable: false),
                canonical_key = table.Column<string>(maxLength: 160, nullable: false),
                request_hash = table.Column<string>(maxLength: 64, nullable: false),
                status = table.Column<string>(maxLength: 32, nullable: false),
                lease_owner = table.Column<string>(maxLength: 64, nullable: true),
                lease_expires_at = table.Column<DateTime>(nullable: true),
                response_json = table.Column<string>(type: "TEXT", nullable: true),
                resolved_session_id = table.Column<string>(nullable: true),
                created_at = table.Column<DateTime>(nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                updated_at = table.Column<DateTime>(nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                completed_at = table.Column<DateTime>(nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_agent_chat_request_receipts", x => x.id);
                table.ForeignKey(
                    name: "FK_agent_chat_request_receipts_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex(
            name: "ux_agent_chat_receipt_scope_key",
            table: "agent_chat_request_receipts",
            columns: new[] { "user_id", "requested_session_id", "canonical_key" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "idx_agent_chat_receipt_status",
            table: "agent_chat_request_receipts",
            columns: new[] { "status", "lease_expires_at" });

        migrationBuilder.CreateTable(
            name: "knowledge_catalog_states",
            columns: table => new
            {
                user_id = table.Column<string>(nullable: false),
                revision = table.Column<long>(nullable: false),
                active_entry_count = table.Column<int>(nullable: false),
                updated_at = table.Column<DateTime>(nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_knowledge_catalog_states", x => x.user_id);
                table.ForeignKey(
                    name: "FK_knowledge_catalog_states_users_user_id",
                    column: x => x.user_id,
                    principalTable: "users",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.Sql("""
            INSERT INTO knowledge_catalog_states (user_id, revision, active_entry_count, updated_at)
            SELECT u.id, 1, COALESCE(SUM(CASE WHEN k.is_archived IS FALSE THEN 1 ELSE 0 END), 0), CURRENT_TIMESTAMP
            FROM users u
            LEFT JOIN knowledge_base k ON k.user_id = u.id
            GROUP BY u.id;
            """);
        migrationBuilder.Sql("UPDATE kernel_tasks SET task_type = 'AcceptanceGate' WHERE task_type = 'UserAcceptance';");
        migrationBuilder.Sql("UPDATE task_graph_versions SET graph_json = replace(graph_json, 'UserAcceptance', 'AcceptanceGate') WHERE graph_json LIKE '%UserAcceptance%';");
        migrationBuilder.Sql("UPDATE production_batches SET acceptance_actor = 'user' WHERE acceptance_actor = 'human';");
        migrationBuilder.Sql("UPDATE production_batches SET acceptance_actor = 'agent-policy' WHERE acceptance_actor = 'agent';");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("UPDATE production_batches SET acceptance_actor = 'human' WHERE acceptance_actor = 'user';");
        migrationBuilder.Sql("UPDATE production_batches SET acceptance_actor = 'agent' WHERE acceptance_actor = 'agent-policy';");
        migrationBuilder.Sql("UPDATE task_graph_versions SET graph_json = replace(graph_json, 'AcceptanceGate', 'UserAcceptance') WHERE graph_json LIKE '%AcceptanceGate%';");
        migrationBuilder.Sql("UPDATE kernel_tasks SET task_type = 'UserAcceptance' WHERE task_type = 'AcceptanceGate';");
        migrationBuilder.DropTable(name: "agent_chat_request_receipts");
        migrationBuilder.DropTable(name: "knowledge_catalog_states");
    }
}
