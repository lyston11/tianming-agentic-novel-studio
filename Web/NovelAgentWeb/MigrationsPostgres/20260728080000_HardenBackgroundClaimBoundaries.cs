using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.MigrationsPostgres;

[DbContext(typeof(PostgresNovelAgentDbContext))]
[Migration("20260728080000_HardenBackgroundClaimBoundaries")]
public sealed class HardenBackgroundClaimBoundaries : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "failure_kind",
            table: "kernel_tasks",
            type: "text",
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "last_error",
            table: "kernel_tasks",
            type: "text",
            nullable: true);
        migrationBuilder.AddColumn<DateTime>(
            name: "next_attempt_at",
            table: "kernel_tasks",
            type: "timestamp with time zone",
            nullable: true);
        migrationBuilder.DropIndex(
            name: "ix_kernel_tasks_claim",
            table: "kernel_tasks");
        migrationBuilder.CreateIndex(
            name: "ix_kernel_tasks_claim",
            table: "kernel_tasks",
            columns: new[] { "status", "next_attempt_at", "priority", "lease_expires_at", "created_at" });

        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION claim_kernel_task(
                p_worker_id text,
                p_lease_seconds integer)
            RETURNS TABLE(
                task_id text,
                user_id text,
                project_id text,
                goal_id text,
                task_graph_version_id text,
                branch_id text,
                kernel_name text,
                task_type text,
                attempt integer,
                lease_owner text,
                lease_expires_at timestamp with time zone)
            LANGUAGE plpgsql
            SECURITY DEFINER
            SET search_path = public, pg_temp
            SET row_security = off
            AS $claim$
            BEGIN
                IF NULLIF(current_setting('app.current_user_id', true), '') IS NOT NULL THEN
                    RAISE EXCEPTION 'claim_kernel_task is restricted to background connections without tenant scope';
                END IF;
                IF p_worker_id IS NULL OR btrim(p_worker_id) = '' THEN
                    RAISE EXCEPTION 'worker id is required';
                END IF;
                IF p_lease_seconds < 5 OR p_lease_seconds > 3600 THEN
                    RAISE EXCEPTION 'lease seconds must be between 5 and 3600';
                END IF;

                RETURN QUERY
                WITH candidate AS (
                    SELECT candidate_task.id
                    FROM kernel_tasks AS candidate_task
                    JOIN task_graph_versions AS candidate_graph
                      ON candidate_graph.id = candidate_task.task_graph_version_id
                     AND candidate_graph.user_id = candidate_task.user_id
                     AND candidate_graph.goal_id = candidate_task.goal_id
                     AND candidate_graph.status = 'active'
                    JOIN creative_goals AS candidate_goal
                      ON candidate_goal.id = candidate_task.goal_id
                     AND candidate_goal.user_id = candidate_task.user_id
                     AND candidate_goal.project_id = candidate_task.project_id
                    WHERE candidate_goal.status IN ('committed', 'running', 'resumed')
                      AND (
                            (
                                candidate_task.status IN ('ready', 'queued')
                                AND (
                                    candidate_task.next_attempt_at IS NULL
                                    OR candidate_task.next_attempt_at <= clock_timestamp()
                                )
                            )
                            OR (
                                candidate_task.status = 'running'
                                AND candidate_task.lease_expires_at < clock_timestamp()
                            )
                          )
                      AND candidate_task.attempt < candidate_task.max_attempts
                    ORDER BY candidate_task.priority, candidate_task.created_at, candidate_task.id
                    FOR UPDATE OF candidate_task SKIP LOCKED
                    LIMIT 1
                )
                UPDATE kernel_tasks AS claimed
                SET status = 'running',
                    lease_owner = p_worker_id,
                    lease_expires_at = clock_timestamp() + make_interval(secs => p_lease_seconds),
                    attempt = claimed.attempt + 1,
                    started_at = COALESCE(claimed.started_at, clock_timestamp()),
                    updated_at = clock_timestamp()
                FROM candidate
                WHERE claimed.id = candidate.id
                RETURNING
                    claimed.id,
                    claimed.user_id,
                    claimed.project_id,
                    claimed.goal_id,
                    claimed.task_graph_version_id,
                    claimed.branch_id,
                    claimed.kernel_name,
                    claimed.task_type,
                    claimed.attempt,
                    claimed.lease_owner,
                    claimed.lease_expires_at;
            END
            $claim$;

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
            SET row_security = off
            AS $claim$
            BEGIN
                IF NULLIF(current_setting('app.current_user_id', true), '') IS NOT NULL THEN
                    RAISE EXCEPTION 'claim_knowledge_processing_task is restricted to background connections without tenant scope';
                END IF;
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

            CREATE OR REPLACE FUNCTION claim_stale_model_execution(p_stale_seconds integer)
            RETURNS TABLE(execution_id text, user_id text)
            LANGUAGE plpgsql
            SECURITY DEFINER
            SET search_path = public, pg_temp
            SET row_security = off
            AS $recovery$
            BEGIN
                IF NULLIF(current_setting('app.current_user_id', true), '') IS NOT NULL THEN
                    RAISE EXCEPTION 'claim_stale_model_execution is restricted to background connections without tenant scope';
                END IF;
                IF p_stale_seconds < 30 OR p_stale_seconds > 86400 THEN
                    RAISE EXCEPTION 'stale seconds must be between 30 and 86400';
                END IF;

                RETURN QUERY
                WITH candidate AS (
                    SELECT execution.id
                    FROM model_executions AS execution
                    WHERE execution.status IN ('reserved', 'running')
                      AND execution.created_at < clock_timestamp() - make_interval(secs => p_stale_seconds)
                    ORDER BY execution.created_at, execution.id
                    FOR UPDATE SKIP LOCKED
                    LIMIT 1
                )
                UPDATE model_executions AS claimed
                SET status = 'outcome_unknown'
                FROM candidate
                WHERE claimed.id = candidate.id
                RETURNING claimed.id, claimed.user_id;
            END
            $recovery$;

            REVOKE ALL ON FUNCTION claim_kernel_task(text, integer) FROM PUBLIC;
            REVOKE ALL ON FUNCTION claim_knowledge_processing_task(text, integer) FROM PUBLIC;
            REVOKE ALL ON FUNCTION claim_stale_model_execution(integer) FROM PUBLIC;
            REVOKE ALL ON FUNCTION claim_outbox_events(text, integer, integer) FROM PUBLIC;
            REVOKE ALL ON FUNCTION list_active_runtime_sessions(integer) FROM PUBLIC;
            REVOKE ALL ON FUNCTION list_queued_runtime_runs(integer) FROM PUBLIC;
            REVOKE ALL ON FUNCTION claim_runtime_run(text) FROM PUBLIC;
            REVOKE ALL ON FUNCTION fail_stale_runtime_runs(integer, text) FROM PUBLIC;
            REVOKE ALL ON FUNCTION fail_running_tool_executions(text) FROM PUBLIC;

            REVOKE ALL ON FUNCTION claim_kernel_task(text, integer) FROM novelagent_app;
            REVOKE ALL ON FUNCTION claim_knowledge_processing_task(text, integer) FROM novelagent_app;
            REVOKE ALL ON FUNCTION claim_stale_model_execution(integer) FROM novelagent_app;
            REVOKE ALL ON FUNCTION claim_outbox_events(text, integer, integer) FROM novelagent_app;
            REVOKE ALL ON FUNCTION list_active_runtime_sessions(integer) FROM novelagent_app;
            REVOKE ALL ON FUNCTION list_queued_runtime_runs(integer) FROM novelagent_app;
            REVOKE ALL ON FUNCTION claim_runtime_run(text) FROM novelagent_app;
            REVOKE ALL ON FUNCTION fail_stale_runtime_runs(integer, text) FROM novelagent_app;
            REVOKE ALL ON FUNCTION fail_running_tool_executions(text) FROM novelagent_app;

            GRANT EXECUTE ON FUNCTION claim_kernel_task(text, integer) TO novelagent_worker;
            GRANT EXECUTE ON FUNCTION claim_knowledge_processing_task(text, integer) TO novelagent_worker;
            GRANT EXECUTE ON FUNCTION claim_stale_model_execution(integer) TO novelagent_worker;
            GRANT EXECUTE ON FUNCTION claim_outbox_events(text, integer, integer) TO novelagent_worker;
            GRANT EXECUTE ON FUNCTION list_active_runtime_sessions(integer) TO novelagent_worker;
            GRANT EXECUTE ON FUNCTION list_queued_runtime_runs(integer) TO novelagent_worker;
            GRANT EXECUTE ON FUNCTION claim_runtime_run(text) TO novelagent_worker;
            GRANT EXECUTE ON FUNCTION fail_stale_runtime_runs(integer, text) TO novelagent_worker;
            GRANT EXECUTE ON FUNCTION fail_running_tool_executions(text) TO novelagent_worker;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Tenant-scope rejection is intentionally retained during rollback.
    }
}
