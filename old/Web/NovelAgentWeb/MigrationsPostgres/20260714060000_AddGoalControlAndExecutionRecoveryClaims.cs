using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.MigrationsPostgres;

[DbContext(typeof(PostgresNovelAgentDbContext))]
[Migration("20260714060000_AddGoalControlAndExecutionRecoveryClaims")]
public sealed class AddGoalControlAndExecutionRecoveryClaims : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
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
                    JOIN creative_goals AS candidate_goal
                      ON candidate_goal.id = candidate_task.goal_id
                     AND candidate_goal.user_id = candidate_task.user_id
                     AND candidate_goal.project_id = candidate_task.project_id
                    WHERE candidate_goal.status IN ('committed', 'running', 'resumed')
                      AND (
                            candidate_task.status IN ('ready', 'queued')
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

            CREATE OR REPLACE FUNCTION claim_stale_model_execution(p_stale_seconds integer)
            RETURNS TABLE(execution_id text, user_id text)
            LANGUAGE plpgsql
            SECURITY DEFINER
            SET search_path = public, pg_temp
            SET row_security = off
            AS $recovery$
            BEGIN
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

            REVOKE ALL ON FUNCTION claim_stale_model_execution(integer) FROM PUBLIC;
            GRANT EXECUTE ON FUNCTION claim_stale_model_execution(integer) TO novelagent_app;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP FUNCTION IF EXISTS claim_stale_model_execution(integer);

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
                    WHERE (
                            candidate_task.status IN ('ready', 'queued')
                            OR (
                                candidate_task.status = 'running'
                                AND candidate_task.lease_expires_at < clock_timestamp()
                            )
                          )
                      AND candidate_task.attempt < candidate_task.max_attempts
                    ORDER BY candidate_task.priority, candidate_task.created_at, candidate_task.id
                    FOR UPDATE SKIP LOCKED
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
            """);
    }
}
