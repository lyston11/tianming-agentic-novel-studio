using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.MigrationsPostgres
{
    /// <inheritdoc />
    public partial class UnifyAgentProductionArchitecture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_chat_request_receipts",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    requested_session_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    canonical_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    lease_owner = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    lease_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    response_json = table.Column<string>(type: "jsonb", nullable: true),
                    resolved_session_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
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

            migrationBuilder.CreateTable(
                name: "knowledge_catalog_states",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "text", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    active_entry_count = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
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

            migrationBuilder.CreateIndex(
                name: "idx_agent_chat_receipt_status",
                table: "agent_chat_request_receipts",
                columns: new[] { "status", "lease_expires_at" });

            migrationBuilder.CreateIndex(
                name: "ux_agent_chat_receipt_scope_key",
                table: "agent_chat_request_receipts",
                columns: new[] { "user_id", "requested_session_id", "canonical_key" },
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO knowledge_catalog_states (user_id, revision, active_entry_count, updated_at)
                SELECT users.id,
                       1,
                       COUNT(knowledge.id) FILTER (WHERE knowledge.is_archived IS FALSE),
                       clock_timestamp()
                FROM users
                LEFT JOIN knowledge_base AS knowledge ON knowledge.user_id = users.id
                GROUP BY users.id
                ON CONFLICT (user_id) DO NOTHING;

                UPDATE kernel_tasks
                SET task_type = 'AcceptanceGate'
                WHERE task_type = 'UserAcceptance';

                UPDATE task_graph_versions
                SET graph_json = replace(graph_json::text, 'UserAcceptance', 'AcceptanceGate')::jsonb
                WHERE graph_json::text LIKE '%UserAcceptance%';

                UPDATE production_batches SET acceptance_actor = 'user' WHERE acceptance_actor = 'human';
                UPDATE production_batches SET acceptance_actor = 'agent-policy' WHERE acceptance_actor = 'agent';

                ALTER TABLE agent_chat_request_receipts ENABLE ROW LEVEL SECURITY;
                ALTER TABLE agent_chat_request_receipts FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON agent_chat_request_receipts
                    USING (user_id = NULLIF(current_setting('app.current_user_id', true), ''))
                    WITH CHECK (user_id = NULLIF(current_setting('app.current_user_id', true), ''));

                ALTER TABLE knowledge_catalog_states ENABLE ROW LEVEL SECURITY;
                ALTER TABLE knowledge_catalog_states FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON knowledge_catalog_states
                    USING (user_id = NULLIF(current_setting('app.current_user_id', true), ''))
                    WITH CHECK (user_id = NULLIF(current_setting('app.current_user_id', true), ''));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE production_batches SET acceptance_actor = 'human' WHERE acceptance_actor = 'user';
                UPDATE production_batches SET acceptance_actor = 'agent' WHERE acceptance_actor = 'agent-policy';
                UPDATE task_graph_versions
                SET graph_json = replace(graph_json::text, 'AcceptanceGate', 'UserAcceptance')::jsonb
                WHERE graph_json::text LIKE '%AcceptanceGate%';
                UPDATE kernel_tasks SET task_type = 'UserAcceptance' WHERE task_type = 'AcceptanceGate';
                """);
            migrationBuilder.DropTable(
                name: "agent_chat_request_receipts");

            migrationBuilder.DropTable(
                name: "knowledge_catalog_states");
        }
    }
}
