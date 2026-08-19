using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Tianming.NovelAgent.Infrastructure.Persistence;

#nullable disable

namespace Tianming.NovelAgent.Infrastructure.Migrations;

[DbContext(typeof(AgentControlDbContext))]
[Migration("202608180001_OwnKernelTaskWorkerClaim")]
public sealed class OwnKernelTaskWorkerClaim : Migration
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

            COMMENT ON FUNCTION claim_kernel_task(text, integer)
                IS 'AgentControlDbContext worker ownership';
            REVOKE ALL ON FUNCTION claim_kernel_task(text, integer) FROM PUBLIC;

            DO $grants$
            BEGIN
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'novelagent_app') THEN
                    REVOKE ALL ON FUNCTION claim_kernel_task(text, integer) FROM novelagent_app;
                END IF;
                IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'novelagent_worker') THEN
                    GRANT EXECUTE ON FUNCTION claim_kernel_task(text, integer) TO novelagent_worker;
                END IF;
            END $grants$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            COMMENT ON FUNCTION claim_kernel_task(text, integer) IS NULL;
            """);
    }
}
