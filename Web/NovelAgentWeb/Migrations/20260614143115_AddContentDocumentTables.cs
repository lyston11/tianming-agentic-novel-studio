using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddContentDocumentTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tags",
                table: "knowledge_base",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "weight",
                table: "knowledge_base",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

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
                        name: "FK_content_vector_points_content_chunks_chunk_id",
                        column: x => x.chunk_id,
                        principalTable: "content_chunks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_content_vector_points_content_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "content_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_content_chunks_doc_index",
                table: "content_chunks",
                columns: new[] { "document_id", "chunk_index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_content_docs_source",
                table: "content_documents",
                columns: new[] { "source_type", "source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_content_docs_status",
                table: "content_documents",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "idx_content_docs_user_project",
                table: "content_documents",
                columns: new[] { "user_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "IX_content_documents_project_id",
                table: "content_documents",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "idx_vector_points_chunk",
                table: "content_vector_points",
                column: "chunk_id");

            migrationBuilder.CreateIndex(
                name: "idx_vector_points_doc",
                table: "content_vector_points",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "idx_vector_points_qdrant",
                table: "content_vector_points",
                columns: new[] { "qdrant_collection", "qdrant_point_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_vector_points_status",
                table: "content_vector_points",
                column: "index_status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "content_vector_points");

            migrationBuilder.DropTable(
                name: "content_chunks");

            migrationBuilder.DropTable(
                name: "content_documents");

            migrationBuilder.DropColumn(
                name: "tags",
                table: "knowledge_base");

            migrationBuilder.DropColumn(
                name: "weight",
                table: "knowledge_base");
        }
    }
}
