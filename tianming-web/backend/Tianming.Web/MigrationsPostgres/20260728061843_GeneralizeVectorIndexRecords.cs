using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.MigrationsPostgres
{
    /// <inheritdoc />
    public partial class GeneralizeVectorIndexRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_vector_index_records_source_version",
                table: "vector_index_records");

            migrationBuilder.AlterColumn<string>(
                name: "document_blob_id",
                table: "vector_index_records",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<int>(
                name: "chunk_index",
                table: "vector_index_records",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_document_id",
                table: "vector_index_records",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE vector_index_records
                SET source_document_id = document_blob_id
                WHERE source_document_id = '' AND document_blob_id IS NOT NULL;

                ALTER TABLE vector_index_records
                ALTER COLUMN source_document_id DROP DEFAULT;
                """);

            migrationBuilder.CreateIndex(
                name: "ux_vector_index_records_source_version",
                table: "vector_index_records",
                columns: new[] { "user_id", "source_document_id", "source_type", "source_id", "chunk_index", "embedding_version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_vector_index_records_source_version",
                table: "vector_index_records");

            migrationBuilder.DropColumn(
                name: "chunk_index",
                table: "vector_index_records");

            migrationBuilder.DropColumn(
                name: "source_document_id",
                table: "vector_index_records");

            migrationBuilder.Sql(
                "DELETE FROM vector_index_records WHERE document_blob_id IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "document_blob_id",
                table: "vector_index_records",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_vector_index_records_source_version",
                table: "vector_index_records",
                columns: new[] { "user_id", "document_blob_id", "source_type", "source_id", "embedding_version" },
                unique: true);
        }
    }
}
