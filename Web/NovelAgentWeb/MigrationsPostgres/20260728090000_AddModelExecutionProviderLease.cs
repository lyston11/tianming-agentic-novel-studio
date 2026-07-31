using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TM.Web.NovelAgentWeb.Data;

#nullable disable

namespace TM.Web.NovelAgentWeb.MigrationsPostgres;

[DbContext(typeof(PostgresNovelAgentDbContext))]
[Migration("20260728090000_AddModelExecutionProviderLease")]
public sealed class AddModelExecutionProviderLease : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "started_at",
            table: "model_executions",
            type: "timestamp with time zone",
            nullable: true);
        migrationBuilder.AddColumn<DateTime>(
            name: "lease_expires_at",
            table: "model_executions",
            type: "timestamp with time zone",
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "provider_lease_owner",
            table: "model_executions",
            type: "text",
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "operation_key",
            table: "model_executions",
            type: "text",
            nullable: true);
        migrationBuilder.Sql("""
            UPDATE model_executions
            SET operation_key = 'legacy:' || id
            WHERE operation_key IS NULL;
            """);
        migrationBuilder.AlterColumn<string>(
            name: "operation_key",
            table: "model_executions",
            type: "text",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "text",
            oldNullable: true);
        migrationBuilder.CreateIndex(
            name: "ix_model_executions_recovery",
            table: "model_executions",
            columns: new[] { "status", "lease_expires_at", "created_at" });

        migrationBuilder.Sql("""
            UPDATE model_executions
            SET started_at = COALESCE(started_at, created_at),
                lease_expires_at = clock_timestamp() + interval '10 minutes'
            WHERE status = 'running'
              AND lease_expires_at IS NULL;

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
                    WHERE (
                            execution.status = 'reserved'
                            AND execution.created_at < clock_timestamp() - make_interval(secs => p_stale_seconds)
                          )
                       OR (
                            execution.status = 'running'
                            AND execution.lease_expires_at IS NOT NULL
                            AND execution.lease_expires_at <= clock_timestamp()
                          )
                    ORDER BY execution.created_at, execution.id
                    FOR UPDATE SKIP LOCKED
                    LIMIT 1
                )
                UPDATE model_executions AS claimed
                SET status = 'outcome_unknown',
                    lease_expires_at = NULL
                FROM candidate
                WHERE claimed.id = candidate.id
                RETURNING claimed.id, claimed.user_id;
            END
            $recovery$;

            CREATE OR REPLACE FUNCTION settle_next_model_execution()
            RETURNS TABLE(execution_id text, user_id text)
            LANGUAGE plpgsql
            SECURITY DEFINER
            SET search_path = public, pg_temp
            SET row_security = off
            AS $settlement$
            DECLARE
                candidate model_executions%ROWTYPE;
            BEGIN
                IF NULLIF(current_setting('app.current_user_id', true), '') IS NOT NULL THEN
                    RAISE EXCEPTION 'settle_next_model_execution is restricted to background connections without tenant scope';
                END IF;

                SELECT execution.*
                INTO candidate
                FROM model_executions AS execution
                WHERE execution.status = 'result_received'
                ORDER BY execution.created_at, execution.id
                FOR UPDATE SKIP LOCKED
                LIMIT 1;
                IF NOT FOUND THEN
                    RETURN;
                END IF;

                UPDATE creative_goals AS goal
                SET reserved_cost = GREATEST(0, goal.reserved_cost - candidate.reserved_cost),
                    actual_cost = goal.actual_cost + candidate.actual_cost,
                    aggregate_version = goal.aggregate_version + 1,
                    status = CASE
                        WHEN goal.actual_cost + candidate.actual_cost >=
                            COALESCE(
                                (
                                    SELECT (revision.constraint_changes_json::jsonb ->> 'totalCostLimit')::numeric
                                    FROM goal_revisions AS revision
                                    WHERE revision.user_id = goal.user_id
                                      AND revision.goal_id = goal.id
                                      AND revision.constraint_changes_json::jsonb ? 'totalCostLimit'
                                      AND NULLIF(revision.constraint_changes_json::jsonb ->> 'totalCostLimit', '') IS NOT NULL
                                    ORDER BY revision.revision_number DESC
                                    LIMIT 1
                                ),
                                goal.total_cost_limit)
                        THEN 'budget_exceeded'
                        ELSE goal.status
                    END
                WHERE goal.id = candidate.goal_id
                  AND goal.user_id = candidate.user_id
                  AND goal.reserved_cost >= candidate.reserved_cost;
                IF NOT FOUND THEN
                    UPDATE model_executions AS execution
                    SET status = 'settlement_failed',
                        error_message = 'Goal budget settlement failed: reserved amount or tenant scope did not match.',
                        lease_expires_at = NULL
                    WHERE execution.id = candidate.id
                      AND execution.user_id = candidate.user_id
                      AND execution.status = 'result_received';
                    RETURN QUERY SELECT candidate.id, candidate.user_id;
                    RETURN;
                END IF;

                UPDATE model_executions AS execution
                SET status = 'completed',
                    lease_expires_at = NULL,
                    completed_at = clock_timestamp()
                WHERE execution.id = candidate.id
                  AND execution.user_id = candidate.user_id
                  AND execution.status = 'result_received';
                IF NOT FOUND THEN
                    RAISE EXCEPTION 'Model execution settlement state changed for %', candidate.id;
                END IF;

                RETURN QUERY SELECT candidate.id, candidate.user_id;
            END
            $settlement$;

            CREATE OR REPLACE FUNCTION fail_stale_tool_executions(
                p_stale_seconds integer,
                p_reason text)
            RETURNS TABLE(execution_id text, user_id text, session_id text, project_id text)
            LANGUAGE plpgsql
            SECURITY DEFINER
            SET search_path = public, pg_temp
            SET row_security = off
            AS $cleanup$
            BEGIN
                IF NULLIF(current_setting('app.current_user_id', true), '') IS NOT NULL THEN
                    RAISE EXCEPTION 'fail_stale_tool_executions is restricted to background connections without tenant scope';
                END IF;
                IF p_stale_seconds < 30 OR p_stale_seconds > 86400 THEN
                    RAISE EXCEPTION 'stale seconds must be between 30 and 86400';
                END IF;
                RETURN QUERY
                WITH stale AS (
                    SELECT execution.id
                    FROM agent_tool_executions AS execution
                    WHERE execution.status = 'running'
                      AND execution.started_at <= clock_timestamp() - make_interval(secs => p_stale_seconds)
                    ORDER BY execution.started_at, execution.id
                    FOR UPDATE SKIP LOCKED
                    LIMIT 100
                )
                UPDATE agent_tool_executions AS execution
                SET status = 'failed',
                    result_phase = 'runtime_cancelled',
                    result_message = p_reason,
                    error_type = 'runtime_cancelled',
                    error_message = p_reason,
                    completed_at = clock_timestamp(),
                    duration_ms = LEAST(
                        2147483647,
                        GREATEST(0, EXTRACT(EPOCH FROM (clock_timestamp() - execution.started_at)) * 1000)
                    )::integer
                FROM stale
                WHERE execution.id = stale.id
                  AND execution.status = 'running'
                RETURNING execution.id, execution.user_id, execution.session_id, execution.project_id;
            END
            $cleanup$;

            REVOKE ALL ON FUNCTION claim_stale_model_execution(integer) FROM PUBLIC;
            REVOKE ALL ON FUNCTION claim_stale_model_execution(integer) FROM novelagent_app;
            GRANT EXECUTE ON FUNCTION claim_stale_model_execution(integer) TO novelagent_worker;
            REVOKE ALL ON FUNCTION settle_next_model_execution() FROM PUBLIC;
            REVOKE ALL ON FUNCTION settle_next_model_execution() FROM novelagent_app;
            GRANT EXECUTE ON FUNCTION settle_next_model_execution() TO novelagent_worker;
            REVOKE ALL ON FUNCTION fail_stale_tool_executions(integer, text) FROM PUBLIC;
            REVOKE ALL ON FUNCTION fail_stale_tool_executions(integer, text) FROM novelagent_app;
            GRANT EXECUTE ON FUNCTION fail_stale_tool_executions(integer, text) TO novelagent_worker;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP FUNCTION IF EXISTS settle_next_model_execution();
            DROP FUNCTION IF EXISTS fail_stale_tool_executions(integer, text);

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
            REVOKE ALL ON FUNCTION claim_stale_model_execution(integer) FROM novelagent_app;
            GRANT EXECUTE ON FUNCTION claim_stale_model_execution(integer) TO novelagent_worker;
            """);
        migrationBuilder.DropIndex(
            name: "ix_model_executions_recovery",
            table: "model_executions");
        migrationBuilder.DropColumn(
            name: "lease_expires_at",
            table: "model_executions");
        migrationBuilder.DropColumn(
            name: "provider_lease_owner",
            table: "model_executions");
        migrationBuilder.DropColumn(
            name: "operation_key",
            table: "model_executions");
        migrationBuilder.DropColumn(
            name: "started_at",
            table: "model_executions");
    }
}
