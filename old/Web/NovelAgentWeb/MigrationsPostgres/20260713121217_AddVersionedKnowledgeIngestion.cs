using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.MigrationsPostgres
{
    /// <inheritdoc />
    public partial class AddVersionedKnowledgeIngestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "upload_blob_id",
                table: "knowledge_processing_tasks",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "knowledge_chunks",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    document_blob_id = table.Column<string>(type: "text", nullable: false),
                    section_id = table.Column<string>(type: "text", nullable: false),
                    knowledge_version = table.Column<long>(type: "bigint", nullable: false),
                    chunk_index = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    char_start = table.Column<int>(type: "integer", nullable: false),
                    char_end = table.Column<int>(type: "integer", nullable: false),
                    content_hash = table.Column<string>(type: "text", nullable: false),
                    previous_chunk_id = table.Column<string>(type: "text", nullable: true),
                    next_chunk_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_chunks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_citations",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    knowledge_entry_id = table.Column<string>(type: "text", nullable: false),
                    knowledge_version = table.Column<long>(type: "bigint", nullable: false),
                    goal_id = table.Column<string>(type: "text", nullable: true),
                    chapter_id = table.Column<string>(type: "text", nullable: true),
                    chapter_version_id = table.Column<string>(type: "text", nullable: true),
                    purpose = table.Column<string>(type: "text", nullable: false),
                    source_artifact_id = table.Column<string>(type: "text", nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_citations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_document_blobs",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    mime_type = table.Column<string>(type: "text", nullable: false),
                    data = table.Column<byte[]>(type: "bytea", nullable: false),
                    content_hash = table.Column<string>(type: "text", nullable: false),
                    knowledge_version = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_document_blobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_entries",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    logical_knowledge_id = table.Column<string>(type: "text", nullable: false),
                    document_blob_id = table.Column<string>(type: "text", nullable: false),
                    knowledge_version = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    source_entry_index = table.Column<int>(type: "integer", nullable: false),
                    entry_type = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    summary = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_sections",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    document_blob_id = table.Column<string>(type: "text", nullable: false),
                    knowledge_version = table.Column<long>(type: "bigint", nullable: false),
                    section_index = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    summary = table.Column<string>(type: "text", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    char_start = table.Column<int>(type: "integer", nullable: false),
                    char_end = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_sections", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "style_profiles",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    document_blob_id = table.Column<string>(type: "text", nullable: false),
                    knowledge_version = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    profile_kind = table.Column<string>(type: "text", nullable: false),
                    features_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_style_profiles", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_chunks_section_order",
                table: "knowledge_chunks",
                columns: new[] { "user_id", "section_id", "char_start" });

            migrationBuilder.CreateIndex(
                name: "ux_knowledge_chunks_document_index",
                table: "knowledge_chunks",
                columns: new[] { "user_id", "document_blob_id", "chunk_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_citations_chapter_version",
                table: "knowledge_citations",
                columns: new[] { "user_id", "project_id", "chapter_version_id" });

            migrationBuilder.CreateIndex(
                name: "ux_knowledge_citations_idempotency",
                table: "knowledge_citations",
                columns: new[] { "user_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_knowledge_document_blobs_user_version",
                table: "knowledge_document_blobs",
                columns: new[] { "user_id", "knowledge_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_knowledge_entries_status_version",
                table: "knowledge_entries",
                columns: new[] { "user_id", "status", "knowledge_version" });

            migrationBuilder.CreateIndex(
                name: "ux_knowledge_entries_logical_version",
                table: "knowledge_entries",
                columns: new[] { "user_id", "logical_knowledge_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_knowledge_sections_document_index",
                table: "knowledge_sections",
                columns: new[] { "user_id", "document_blob_id", "section_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_style_profiles_document_version",
                table: "style_profiles",
                columns: new[] { "user_id", "document_blob_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_processing_tasks_upload_blob_id",
                table: "knowledge_processing_tasks",
                column: "upload_blob_id");

            migrationBuilder.CreateIndex(name: "IX_knowledge_chunks_document_blob_id", table: "knowledge_chunks", column: "document_blob_id");
            migrationBuilder.CreateIndex(name: "IX_knowledge_chunks_section_id", table: "knowledge_chunks", column: "section_id");
            migrationBuilder.CreateIndex(name: "IX_knowledge_citations_knowledge_entry_id", table: "knowledge_citations", column: "knowledge_entry_id");
            migrationBuilder.CreateIndex(name: "IX_knowledge_entries_document_blob_id", table: "knowledge_entries", column: "document_blob_id");
            migrationBuilder.CreateIndex(name: "IX_knowledge_entries_logical_knowledge_id", table: "knowledge_entries", column: "logical_knowledge_id");
            migrationBuilder.CreateIndex(name: "IX_knowledge_sections_document_blob_id", table: "knowledge_sections", column: "document_blob_id");
            migrationBuilder.CreateIndex(name: "IX_style_profiles_document_blob_id", table: "style_profiles", column: "document_blob_id");

            migrationBuilder.AddForeignKey(
                name: "FK_knowledge_processing_tasks_knowledge_document_blobs_upload_blob_id",
                table: "knowledge_processing_tasks",
                column: "upload_blob_id",
                principalTable: "knowledge_document_blobs",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
            migrationBuilder.AddForeignKey(
                name: "FK_knowledge_sections_knowledge_document_blobs_document_blob_id",
                table: "knowledge_sections",
                column: "document_blob_id",
                principalTable: "knowledge_document_blobs",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
            migrationBuilder.AddForeignKey(
                name: "FK_knowledge_chunks_knowledge_document_blobs_document_blob_id",
                table: "knowledge_chunks",
                column: "document_blob_id",
                principalTable: "knowledge_document_blobs",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
            migrationBuilder.AddForeignKey(
                name: "FK_knowledge_chunks_knowledge_sections_section_id",
                table: "knowledge_chunks",
                column: "section_id",
                principalTable: "knowledge_sections",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
            migrationBuilder.AddForeignKey(
                name: "FK_knowledge_entries_knowledge_document_blobs_document_blob_id",
                table: "knowledge_entries",
                column: "document_blob_id",
                principalTable: "knowledge_document_blobs",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
            migrationBuilder.AddForeignKey(
                name: "FK_knowledge_entries_knowledge_base_logical_knowledge_id",
                table: "knowledge_entries",
                column: "logical_knowledge_id",
                principalTable: "knowledge_base",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "FK_style_profiles_knowledge_document_blobs_document_blob_id",
                table: "style_profiles",
                column: "document_blob_id",
                principalTable: "knowledge_document_blobs",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
            migrationBuilder.AddForeignKey(
                name: "FK_knowledge_citations_knowledge_entries_knowledge_entry_id",
                table: "knowledge_citations",
                column: "knowledge_entry_id",
                principalTable: "knowledge_entries",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                DO $tenant_rls$
                DECLARE
                    target_table text;
                    target_tables text[] := ARRAY[
                        'knowledge_document_blobs',
                        'knowledge_sections',
                        'knowledge_chunks',
                        'knowledge_entries',
                        'style_profiles',
                        'knowledge_citations'
                    ];
                BEGIN
                    FOREACH target_table IN ARRAY target_tables LOOP
                        EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY', target_table);
                        EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY', target_table);
                        EXECUTE format(
                            'CREATE POLICY tenant_isolation ON %I USING (user_id = NULLIF(current_setting(''app.current_user_id'', true), '''')) WITH CHECK (user_id = NULLIF(current_setting(''app.current_user_id'', true), ''''))',
                            target_table);
                    END LOOP;
                END
                $tenant_rls$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_knowledge_processing_tasks_knowledge_document_blobs_upload_blob_id",
                table: "knowledge_processing_tasks");

            migrationBuilder.DropIndex(
                name: "IX_knowledge_processing_tasks_upload_blob_id",
                table: "knowledge_processing_tasks");

            migrationBuilder.DropTable(
                name: "knowledge_chunks");

            migrationBuilder.DropTable(
                name: "knowledge_citations");

            migrationBuilder.DropTable(
                name: "style_profiles");

            migrationBuilder.DropTable(
                name: "knowledge_entries");

            migrationBuilder.DropTable(
                name: "knowledge_sections");

            migrationBuilder.DropTable(
                name: "knowledge_document_blobs");

            migrationBuilder.DropColumn(
                name: "upload_blob_id",
                table: "knowledge_processing_tasks");
        }
    }
}
