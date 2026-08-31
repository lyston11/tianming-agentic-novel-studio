using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(NovelAgentDbContext))]
    [Migration("20260615090000_EnforceAgentMemoryUniqueness")]
    public partial class EnforceAgentMemoryUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE agent_memories
                SET project_id = NULL
                WHERE session_id IS NOT NULL
                  AND memory_type LIKE 'session.%';
                """);

            migrationBuilder.Sql("""
                DELETE FROM agent_memories
                WHERE id IN (
                    SELECT id
                    FROM (
                        SELECT
                            id,
                            ROW_NUMBER() OVER (
                                PARTITION BY user_id, memory_type
                                ORDER BY updated_at DESC, created_at DESC, id DESC
                            ) AS duplicate_rank
                        FROM agent_memories
                        WHERE project_id IS NULL
                          AND session_id IS NULL
                    )
                    WHERE duplicate_rank > 1
                );
                """);

            migrationBuilder.Sql("""
                DELETE FROM agent_memories
                WHERE id IN (
                    SELECT id
                    FROM (
                        SELECT
                            id,
                            ROW_NUMBER() OVER (
                                PARTITION BY user_id, project_id, memory_type
                                ORDER BY updated_at DESC, created_at DESC, id DESC
                            ) AS duplicate_rank
                        FROM agent_memories
                        WHERE project_id IS NOT NULL
                          AND session_id IS NULL
                    )
                    WHERE duplicate_rank > 1
                );
                """);

            migrationBuilder.Sql("""
                DELETE FROM agent_memories
                WHERE id IN (
                    SELECT id
                    FROM (
                        SELECT
                            id,
                            ROW_NUMBER() OVER (
                                PARTITION BY user_id, session_id, memory_type
                                ORDER BY updated_at DESC, created_at DESC, id DESC
                            ) AS duplicate_rank
                        FROM agent_memories
                        WHERE session_id IS NOT NULL
                    )
                    WHERE duplicate_rank > 1
                );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_agent_memories_user_type_global",
                table: "agent_memories",
                columns: new[] { "user_id", "memory_type" },
                unique: true,
                filter: "project_id IS NULL AND session_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_agent_memories_user_project_type",
                table: "agent_memories",
                columns: new[] { "user_id", "project_id", "memory_type" },
                unique: true,
                filter: "project_id IS NOT NULL AND session_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_agent_memories_user_session_type",
                table: "agent_memories",
                columns: new[] { "user_id", "session_id", "memory_type" },
                unique: true,
                filter: "session_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_agent_memories_user_type_global",
                table: "agent_memories");

            migrationBuilder.DropIndex(
                name: "IX_agent_memories_user_project_type",
                table: "agent_memories");

            migrationBuilder.DropIndex(
                name: "IX_agent_memories_user_session_type",
                table: "agent_memories");
        }
    }
}
