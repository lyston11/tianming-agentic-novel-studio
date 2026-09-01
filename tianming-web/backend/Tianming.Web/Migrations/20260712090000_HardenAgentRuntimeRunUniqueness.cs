using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    [DbContext(typeof(NovelAgentDbContext))]
    [Migration("20260712090000_HardenAgentRuntimeRunUniqueness")]
    public partial class HardenAgentRuntimeRunUniqueness : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE agent_runtime_runs
                SET status = 'failed',
                    current_phase = 'failed',
                    error_message = 'Superseded during active-run uniqueness migration.',
                    last_message = '历史重复运行已由迁移关闭。',
                    completed_at = CURRENT_TIMESTAMP,
                    updated_at = CURRENT_TIMESTAMP
                WHERE id IN (
                    SELECT id
                    FROM (
                        SELECT id,
                               ROW_NUMBER() OVER (
                                   PARTITION BY user_id, session_id
                                   ORDER BY updated_at DESC, created_at DESC, id DESC
                               ) AS duplicate_rank
                        FROM agent_runtime_runs
                        WHERE status IN ('queued', 'running')
                    )
                    WHERE duplicate_rank > 1
                );
                """);

            migrationBuilder.Sql("""
                UPDATE agent_runtime_runs
                SET idempotency_key = ''
                WHERE id IN (
                    SELECT id
                    FROM (
                        SELECT id,
                               ROW_NUMBER() OVER (
                                   PARTITION BY user_id, session_id, idempotency_key
                                   ORDER BY updated_at DESC, created_at DESC, id DESC
                               ) AS duplicate_rank
                        FROM agent_runtime_runs
                        WHERE idempotency_key <> ''
                    )
                    WHERE duplicate_rank > 1
                );
                """);

            migrationBuilder.DropIndex(
                name: "idx_agent_runtime_runs_idempotency",
                table: "agent_runtime_runs");

            migrationBuilder.CreateIndex(
                name: "ux_agent_runtime_runs_idempotency",
                table: "agent_runtime_runs",
                columns: new[] { "user_id", "session_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key <> ''");

            migrationBuilder.CreateIndex(
                name: "ux_agent_runtime_runs_active_session",
                table: "agent_runtime_runs",
                columns: new[] { "user_id", "session_id" },
                unique: true,
                filter: "status IN ('queued', 'running')");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_agent_runtime_runs_idempotency",
                table: "agent_runtime_runs");

            migrationBuilder.DropIndex(
                name: "ux_agent_runtime_runs_active_session",
                table: "agent_runtime_runs");

            migrationBuilder.CreateIndex(
                name: "idx_agent_runtime_runs_idempotency",
                table: "agent_runtime_runs",
                columns: new[] { "user_id", "session_id", "idempotency_key" });
        }
    }
}
