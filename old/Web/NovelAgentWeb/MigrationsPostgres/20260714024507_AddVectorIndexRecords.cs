using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.MigrationsPostgres
{
    /// <inheritdoc />
    public partial class AddVectorIndexRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "vector_index_records",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    project_id = table.Column<string>(type: "text", nullable: false),
                    branch_id = table.Column<string>(type: "text", nullable: false),
                    document_blob_id = table.Column<string>(type: "text", nullable: false),
                    source_type = table.Column<string>(type: "text", nullable: false),
                    source_id = table.Column<string>(type: "text", nullable: false),
                    knowledge_version = table.Column<long>(type: "bigint", nullable: false),
                    content_hash = table.Column<string>(type: "text", nullable: false),
                    embedding_version = table.Column<string>(type: "text", nullable: false),
                    qdrant_collection = table.Column<string>(type: "text", nullable: false),
                    qdrant_point_id = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    indexed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vector_index_records", x => x.id);
                    table.ForeignKey(
                        name: "FK_vector_index_records_knowledge_document_blobs_document_blob~",
                        column: x => x.document_blob_id,
                        principalTable: "knowledge_document_blobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_vector_index_records_document_blob_id",
                table: "vector_index_records",
                column: "document_blob_id");

            migrationBuilder.CreateIndex(
                name: "ix_vector_index_records_status",
                table: "vector_index_records",
                columns: new[] { "user_id", "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ux_vector_index_records_source_version",
                table: "vector_index_records",
                columns: new[] { "user_id", "document_blob_id", "source_type", "source_id", "embedding_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_vector_index_records_user_point",
                table: "vector_index_records",
                columns: new[] { "user_id", "qdrant_point_id" },
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE vector_index_records ENABLE ROW LEVEL SECURITY;
                ALTER TABLE vector_index_records FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON vector_index_records
                    USING (user_id = NULLIF(current_setting('app.current_user_id', true), ''))
                    WITH CHECK (user_id = NULLIF(current_setting('app.current_user_id', true), ''));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vector_index_records");
        }
    }
}
