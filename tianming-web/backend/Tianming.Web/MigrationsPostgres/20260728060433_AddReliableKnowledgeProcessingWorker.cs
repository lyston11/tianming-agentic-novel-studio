using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.MigrationsPostgres
{
    /// <inheritdoc />
    public partial class AddReliableKnowledgeProcessingWorker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "attempt",
                table: "knowledge_processing_tasks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "max_attempts",
                table: "knowledge_processing_tasks",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<DateTime>(
                name: "processing_lease_expires_at",
                table: "knowledge_processing_tasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "processing_owner",
                table: "knowledge_processing_tasks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "processing_stage",
                table: "knowledge_processing_tasks",
                type: "text",
                nullable: false,
                defaultValue: "extract");

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "knowledge_processing_tasks",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.CreateIndex(
                name: "idx_knowledge_processing_tasks_claim",
                table: "knowledge_processing_tasks",
                columns: new[] { "status", "processing_lease_expires_at", "created_at" });

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION claim_knowledge_processing_task(
                    p_worker_id text,
                    p_lease_seconds integer)
                RETURNS TABLE(
                    task_id text,
                    user_id text,
                    document_blob_id text,
                    processing_stage text,
                    attempt integer,
                    lease_owner text,
                    lease_expires_at timestamp with time zone)
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = public, pg_temp
                AS $claim$
                BEGIN
                    IF p_worker_id IS NULL OR btrim(p_worker_id) = '' THEN
                        RAISE EXCEPTION 'worker id must not be empty';
                    END IF;
                    IF p_lease_seconds < 5 OR p_lease_seconds > 3600 THEN
                        RAISE EXCEPTION 'lease seconds must be between 5 and 3600';
                    END IF;

                    RETURN QUERY
                    WITH candidate AS (
                        SELECT candidate_task.id
                        FROM knowledge_processing_tasks AS candidate_task
                        WHERE candidate_task.upload_blob_id IS NOT NULL
                          AND candidate_task.attempt < candidate_task.max_attempts
                          AND (
                                candidate_task.status IN ('pending', 'retryable_failed')
                                OR (
                                    candidate_task.status IN ('claimed', 'processing')
                                    AND candidate_task.processing_lease_expires_at < clock_timestamp()
                                )
                              )
                        ORDER BY candidate_task.created_at, candidate_task.id
                        FOR UPDATE OF candidate_task SKIP LOCKED
                        LIMIT 1
                    )
                    UPDATE knowledge_processing_tasks AS claimed
                    SET status = 'claimed',
                        processing_stage = COALESCE(NULLIF(claimed.processing_stage, ''), 'extract'),
                        processing_owner = p_worker_id,
                        processing_lease_expires_at = clock_timestamp() + make_interval(secs => p_lease_seconds),
                        attempt = claimed.attempt + 1,
                        started_at = COALESCE(claimed.started_at, clock_timestamp()),
                        updated_at = clock_timestamp()
                    FROM candidate
                    WHERE claimed.id = candidate.id
                    RETURNING
                        claimed.id,
                        claimed.user_id,
                        claimed.upload_blob_id,
                        claimed.processing_stage,
                        claimed.attempt,
                        claimed.processing_owner,
                        claimed.processing_lease_expires_at;
                END
                $claim$;

                REVOKE ALL ON FUNCTION claim_knowledge_processing_task(text, integer) FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION claim_knowledge_processing_task(text, integer) TO novelagent_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS claim_knowledge_processing_task(text, integer);");

            migrationBuilder.DropIndex(
                name: "idx_knowledge_processing_tasks_claim",
                table: "knowledge_processing_tasks");

            migrationBuilder.DropColumn(
                name: "attempt",
                table: "knowledge_processing_tasks");

            migrationBuilder.DropColumn(
                name: "max_attempts",
                table: "knowledge_processing_tasks");

            migrationBuilder.DropColumn(
                name: "processing_lease_expires_at",
                table: "knowledge_processing_tasks");

            migrationBuilder.DropColumn(
                name: "processing_owner",
                table: "knowledge_processing_tasks");

            migrationBuilder.DropColumn(
                name: "processing_stage",
                table: "knowledge_processing_tasks");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "knowledge_processing_tasks");
        }
    }
}
