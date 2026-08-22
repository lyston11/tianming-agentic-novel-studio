using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddUnifiedMemoryPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("PRAGMA foreign_keys=OFF;");

            migrationBuilder.CreateTable(
                name: "agent_memories_new",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: true),
                    session_id = table.Column<string>(type: "TEXT", nullable: true),
                    memory_type = table.Column<string>(type: "TEXT", nullable: false),
                    memory_key = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    content = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_memories", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_memories_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_memories_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO agent_memories_new (id, user_id, project_id, session_id, memory_type, memory_key, content, created_at, updated_at)
                SELECT id, user_id, project_id, NULL, memory_type, '', content, COALESCE(updated_at, CURRENT_TIMESTAMP), updated_at
                FROM agent_memories;
                """);

            migrationBuilder.DropTable(name: "agent_memories");
            migrationBuilder.RenameTable(name: "agent_memories_new", newName: "agent_memories");

            migrationBuilder.CreateIndex(
                name: "IX_agent_memories_project_id",
                table: "agent_memories",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "idx_memories_user_project",
                table: "agent_memories",
                columns: new[] { "user_id", "project_id" });

            migrationBuilder.Sql("PRAGMA foreign_keys=ON;");

            migrationBuilder.CreateTable(
                name: "agent_chat_summaries",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    session_id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: true),
                    start_turn = table.Column<int>(type: "INTEGER", nullable: false),
                    end_turn = table.Column<int>(type: "INTEGER", nullable: false),
                    summary_type = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "summary"),
                    content = table.Column<string>(type: "TEXT", nullable: false),
                    key_decisions_json = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_chat_summaries", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_chat_summaries_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_chat_summaries_agent_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "agent_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_chat_summaries_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_chat_turns",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    session_id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: true),
                    turn_index = table.Column<int>(type: "INTEGER", nullable: false),
                    role = table.Column<string>(type: "TEXT", nullable: false),
                    content = table.Column<string>(type: "TEXT", nullable: false),
                    token_count = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    compressed_into_summary_id = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_chat_turns", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_chat_turns_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_chat_turns_agent_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "agent_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_chat_turns_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_memory_events",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: true),
                    session_id = table.Column<string>(type: "TEXT", nullable: true),
                    run_id = table.Column<string>(type: "TEXT", nullable: true),
                    source_type = table.Column<string>(type: "TEXT", nullable: false),
                    trigger_type = table.Column<string>(type: "TEXT", nullable: false),
                    memory_scope = table.Column<string>(type: "TEXT", nullable: false),
                    memory_key = table.Column<string>(type: "TEXT", nullable: false),
                    payload_json = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "{}"),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_memory_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_memory_events_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_memory_events_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_memory_versions",
                columns: table => new
                {
                    id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: true),
                    session_id = table.Column<string>(type: "TEXT", nullable: true),
                    scope = table.Column<string>(type: "TEXT", nullable: false),
                    version = table.Column<long>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_memory_versions", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_memory_versions_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_memory_versions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "content_documents",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: true),
                    source_type = table.Column<string>(type: "TEXT", nullable: false),
                    source_id = table.Column<string>(type: "TEXT", nullable: false),
                    document_role = table.Column<string>(type: "TEXT", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    mime_type = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "text/plain"),
                    content_hash = table.Column<string>(type: "TEXT", nullable: false),
                    version = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "active"),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_documents", x => x.id);
                    table.ForeignKey(
                        name: "FK_content_documents_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_content_documents_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_knowledge_usages",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: false),
                    knowledge_id = table.Column<string>(type: "TEXT", nullable: false),
                    status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "imported"),
                    source_session_id = table.Column<string>(type: "TEXT", nullable: true),
                    source_run_id = table.Column<string>(type: "TEXT", nullable: true),
                    first_seen_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    last_used_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    usage_count = table.Column<int>(type: "INTEGER", nullable: false),
                    note = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_knowledge_usages", x => x.id);
                    table.ForeignKey(
                        name: "FK_project_knowledge_usages_knowledge_base_knowledge_id",
                        column: x => x.knowledge_id,
                        principalTable: "knowledge_base",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_project_knowledge_usages_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_project_knowledge_usages_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "content_chunks",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    document_id = table.Column<string>(type: "TEXT", nullable: false),
                    chunk_index = table.Column<int>(type: "INTEGER", nullable: false),
                    chunk_text = table.Column<string>(type: "TEXT", nullable: false),
                    token_count = table.Column<int>(type: "INTEGER", nullable: false),
                    char_start = table.Column<int>(type: "INTEGER", nullable: false),
                    char_end = table.Column<int>(type: "INTEGER", nullable: false),
                    content_hash = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_chunks", x => x.id);
                    table.UniqueConstraint("AK_content_chunks_id_document_id", x => new { x.id, x.document_id });
                    table.ForeignKey(
                        name: "FK_content_chunks_content_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "content_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "content_vector_points",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    document_id = table.Column<string>(type: "TEXT", nullable: false),
                    chunk_id = table.Column<string>(type: "TEXT", nullable: true),
                    qdrant_collection = table.Column<string>(type: "TEXT", nullable: false),
                    qdrant_point_id = table.Column<string>(type: "TEXT", nullable: false),
                    vector_model = table.Column<string>(type: "TEXT", nullable: false),
                    indexed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    index_status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "pending"),
                    error_message = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_vector_points", x => x.id);
                    table.ForeignKey(
                        name: "FK_content_vector_points_content_chunks_chunk_id_document_id",
                        columns: x => new { x.chunk_id, x.document_id },
                        principalTable: "content_chunks",
                        principalColumns: new[] { "id", "document_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_content_vector_points_content_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "content_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agent_chat_summaries_project_id",
                table: "agent_chat_summaries",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_chat_summaries_session_id",
                table: "agent_chat_summaries",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_chat_summaries_user_id",
                table: "agent_chat_summaries",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_chat_turns_project_id",
                table: "agent_chat_turns",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_chat_turns_session_id_turn_index",
                table: "agent_chat_turns",
                columns: new[] { "session_id", "turn_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_chat_turns_user_id",
                table: "agent_chat_turns",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_memory_events_project_id",
                table: "agent_memory_events",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_memory_events_run_id",
                table: "agent_memory_events",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_memory_events_session_id",
                table: "agent_memory_events",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_memory_events_user_id",
                table: "agent_memory_events",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_memory_versions_project_id",
                table: "agent_memory_versions",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_agent_memory_versions_user_id_project_id_session_id_scope",
                table: "agent_memory_versions",
                columns: new[] { "user_id", "project_id", "session_id", "scope" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agent_memory_versions_user_scope_global",
                table: "agent_memory_versions",
                columns: new[] { "user_id", "scope" },
                unique: true,
                filter: "project_id IS NULL AND session_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_agent_memory_versions_user_project_scope",
                table: "agent_memory_versions",
                columns: new[] { "user_id", "project_id", "scope" },
                unique: true,
                filter: "project_id IS NOT NULL AND session_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_agent_memory_versions_user_session_scope",
                table: "agent_memory_versions",
                columns: new[] { "user_id", "session_id", "scope" },
                unique: true,
                filter: "project_id IS NULL AND session_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_agent_memory_versions_user_project_session_scope_not_null",
                table: "agent_memory_versions",
                columns: new[] { "user_id", "project_id", "session_id", "scope" },
                unique: true,
                filter: "project_id IS NOT NULL AND session_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_content_chunks_document_id_chunk_index",
                table: "content_chunks",
                columns: new[] { "document_id", "chunk_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_content_documents_project_id",
                table: "content_documents",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_content_documents_source_type_source_id_version",
                table: "content_documents",
                columns: new[] { "source_type", "source_id", "version" });

            migrationBuilder.CreateIndex(
                name: "IX_content_documents_user_id",
                table: "content_documents",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_content_vector_points_chunk_id",
                table: "content_vector_points",
                column: "chunk_id");

            migrationBuilder.CreateIndex(
                name: "IX_content_vector_points_chunk_id_document_id",
                table: "content_vector_points",
                columns: new[] { "chunk_id", "document_id" });

            migrationBuilder.CreateIndex(
                name: "IX_content_vector_points_document_id",
                table: "content_vector_points",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "IX_content_vector_points_qdrant_collection_qdrant_point_id",
                table: "content_vector_points",
                columns: new[] { "qdrant_collection", "qdrant_point_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_knowledge_usages_knowledge_id",
                table: "project_knowledge_usages",
                column: "knowledge_id");

            migrationBuilder.CreateIndex(
                name: "IX_project_knowledge_usages_project_id",
                table: "project_knowledge_usages",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "IX_project_knowledge_usages_user_id_project_id_knowledge_id",
                table: "project_knowledge_usages",
                columns: new[] { "user_id", "project_id", "knowledge_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_chat_summaries");

            migrationBuilder.DropTable(
                name: "agent_chat_turns");

            migrationBuilder.DropTable(
                name: "agent_memory_events");

            migrationBuilder.DropTable(
                name: "agent_memory_versions");

            migrationBuilder.DropTable(
                name: "content_vector_points");

            migrationBuilder.DropTable(
                name: "project_knowledge_usages");

            migrationBuilder.DropTable(
                name: "content_chunks");

            migrationBuilder.DropTable(
                name: "content_documents");

            migrationBuilder.Sql("PRAGMA foreign_keys=OFF;");

            migrationBuilder.CreateTable(
                name: "agent_memories_old",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<string>(type: "TEXT", nullable: false),
                    project_id = table.Column<string>(type: "TEXT", nullable: true),
                    memory_type = table.Column<string>(type: "TEXT", nullable: false),
                    content = table.Column<string>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent_memories", x => x.id);
                    table.ForeignKey(
                        name: "FK_agent_memories_novel_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "novel_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_agent_memories_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO agent_memories_old (id, user_id, project_id, memory_type, content, updated_at)
                SELECT id, user_id, project_id, memory_type, content, updated_at
                FROM agent_memories;
                """);

            migrationBuilder.DropTable(name: "agent_memories");
            migrationBuilder.RenameTable(name: "agent_memories_old", newName: "agent_memories");

            migrationBuilder.CreateIndex(
                name: "IX_agent_memories_project_id",
                table: "agent_memories",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "idx_memories_user_project",
                table: "agent_memories",
                columns: new[] { "user_id", "project_id" });

            migrationBuilder.Sql("PRAGMA foreign_keys=ON;");
        }
    }
}
