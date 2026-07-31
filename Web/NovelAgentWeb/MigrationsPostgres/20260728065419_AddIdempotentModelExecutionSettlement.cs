using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TM.Web.NovelAgentWeb.MigrationsPostgres
{
    /// <inheritdoc />
    public partial class AddIdempotentModelExecutionSettlement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "idempotency_key",
                table: "model_executions",
                type: "text",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE model_executions
                SET idempotency_key = 'legacy:' || id
                WHERE idempotency_key IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "idempotency_key",
                table: "model_executions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "result_json",
                table: "model_executions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_model_executions_idempotency",
                table: "model_executions",
                columns: new[] { "user_id", "idempotency_key" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION claim_outbox_events(
                    p_owner text,
                    p_lease_seconds integer,
                    p_max_items integer)
                RETURNS TABLE(event_id text, user_id text)
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = public, pg_temp
                SET row_security = off
                AS $claim$
                BEGIN
                    IF NULLIF(current_setting('app.current_user_id', true), '') IS NOT NULL THEN
                        RAISE EXCEPTION 'claim_outbox_events is restricted to background connections without tenant scope';
                    END IF;
                    IF p_owner IS NULL OR btrim(p_owner) = '' THEN
                        RAISE EXCEPTION 'processing owner is required';
                    END IF;
                    IF p_lease_seconds < 5 OR p_lease_seconds > 3600 THEN
                        RAISE EXCEPTION 'lease seconds must be between 5 and 3600';
                    END IF;
                    IF p_max_items < 1 OR p_max_items > 100 THEN
                        RAISE EXCEPTION 'max items must be between 1 and 100';
                    END IF;

                    RETURN QUERY
                    WITH candidates AS (
                        SELECT candidate.id
                        FROM outbox_events AS candidate
                        WHERE candidate.status = 'pending'
                           OR (
                                candidate.status = 'retryable_failed'
                                AND (candidate.next_attempt_at IS NULL OR candidate.next_attempt_at <= clock_timestamp())
                              )
                           OR (
                                candidate.status = 'processing'
                                AND (
                                    candidate.processing_lease_expires_at <= clock_timestamp()
                                    OR (
                                        candidate.processing_lease_expires_at IS NULL
                                        AND candidate.updated_at <= clock_timestamp() - make_interval(secs => p_lease_seconds)
                                    )
                                )
                              )
                        ORDER BY candidate.created_at, candidate.id
                        FOR UPDATE SKIP LOCKED
                        LIMIT p_max_items
                    )
                    UPDATE outbox_events AS claimed
                    SET status = 'processing',
                        last_error = NULL,
                        next_attempt_at = NULL,
                        processing_owner = p_owner,
                        processing_lease_expires_at = clock_timestamp() + make_interval(secs => p_lease_seconds),
                        updated_at = clock_timestamp()
                    FROM candidates
                    WHERE claimed.id = candidates.id
                    RETURNING claimed.id, claimed.user_id;
                END
                $claim$;

                CREATE OR REPLACE FUNCTION list_active_runtime_sessions(p_max_sessions integer)
                RETURNS TABLE(
                    runtime_run_id text,
                    user_id text,
                    session_id text,
                    project_id text,
                    updated_at timestamp with time zone)
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = public, pg_temp
                SET row_security = off
                AS $list$
                BEGIN
                    IF NULLIF(current_setting('app.current_user_id', true), '') IS NOT NULL THEN
                        RAISE EXCEPTION 'list_active_runtime_sessions is restricted to background connections without tenant scope';
                    END IF;
                    IF p_max_sessions < 1 OR p_max_sessions > 500 THEN
                        RAISE EXCEPTION 'max sessions must be between 1 and 500';
                    END IF;

                    RETURN QUERY
                    SELECT latest.id::text,
                           latest.user_id::text,
                           latest.session_id::text,
                           latest.project_id::text,
                           latest.updated_at
                    FROM (
                        SELECT DISTINCT ON (run.user_id, run.session_id)
                               run.id,
                               run.user_id,
                               run.session_id,
                               run.project_id,
                               run.updated_at
                        FROM agent_runtime_runs AS run
                        WHERE run.status IN ('queued', 'running')
                        ORDER BY run.user_id, run.session_id, run.updated_at DESC, run.id DESC
                    ) AS latest
                    ORDER BY latest.updated_at DESC, latest.id DESC
                    LIMIT p_max_sessions;
                END
                $list$;

                CREATE OR REPLACE FUNCTION list_queued_runtime_runs(p_max_items integer)
                RETURNS TABLE(runtime_run_id text, user_id text)
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = public, pg_temp
                SET row_security = off
                AS $queued$
                BEGIN
                    IF NULLIF(current_setting('app.current_user_id', true), '') IS NOT NULL THEN
                        RAISE EXCEPTION 'list_queued_runtime_runs is restricted to background connections without tenant scope';
                    END IF;
                    IF p_max_items < 1 OR p_max_items > 1000 THEN
                        RAISE EXCEPTION 'max items must be between 1 and 1000';
                    END IF;

                    RETURN QUERY
                    SELECT run.id::text, run.user_id::text
                    FROM agent_runtime_runs AS run
                    WHERE run.status = 'queued'
                    ORDER BY run.created_at, run.id
                    LIMIT p_max_items;
                END
                $queued$;

                CREATE OR REPLACE FUNCTION claim_runtime_run(p_runtime_run_id text)
                RETURNS TABLE(runtime_run_id text, user_id text)
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = public, pg_temp
                SET row_security = off
                AS $runtime_claim$
                BEGIN
                    IF NULLIF(current_setting('app.current_user_id', true), '') IS NOT NULL THEN
                        RAISE EXCEPTION 'claim_runtime_run is restricted to background connections without tenant scope';
                    END IF;
                    IF p_runtime_run_id IS NULL OR btrim(p_runtime_run_id) = '' THEN
                        RAISE EXCEPTION 'runtime run id is required';
                    END IF;

                    RETURN QUERY
                    UPDATE agent_runtime_runs AS run
                    SET status = 'running',
                        current_phase = 'running',
                        started_at = COALESCE(run.started_at, clock_timestamp()),
                        updated_at = clock_timestamp(),
                        last_message = 'Agent 已开始后台执行。'
                    WHERE run.id = p_runtime_run_id
                      AND run.status = 'queued'
                    RETURNING run.id::text, run.user_id::text;
                END
                $runtime_claim$;

                CREATE OR REPLACE FUNCTION fail_stale_runtime_runs(
                    p_stale_seconds integer,
                    p_reason text)
                RETURNS TABLE(runtime_run_id text, user_id text, session_id text)
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = public, pg_temp
                SET row_security = off
                AS $stale$
                BEGIN
                    IF NULLIF(current_setting('app.current_user_id', true), '') IS NOT NULL THEN
                        RAISE EXCEPTION 'fail_stale_runtime_runs is restricted to background connections without tenant scope';
                    END IF;
                    IF p_stale_seconds < 1 OR p_stale_seconds > 86400 THEN
                        RAISE EXCEPTION 'stale seconds must be between 1 and 86400';
                    END IF;
                    IF p_reason IS NULL OR btrim(p_reason) = '' THEN
                        RAISE EXCEPTION 'failure reason is required';
                    END IF;

                    RETURN QUERY
                    UPDATE agent_runtime_runs AS run
                    SET status = 'failed',
                        current_phase = 'heartbeat_lost',
                        error_message = btrim(p_reason),
                        failure_json = jsonb_build_object(
                            'code', 'RUNTIME_HEARTBEAT_LOST',
                            'stage', 'heartbeat_lost',
                            'message', btrim(p_reason),
                            'recoverable', true,
                            'recommendedAction', 'QueryRuntimeRun 后按当前章节、工作流和工具结果决定恢复、重试或询问用户。',
                            'artifactIds', jsonb_build_array())::text,
                        last_message = '后台运行心跳超时，已进入可恢复失败状态。',
                        completed_at = clock_timestamp(),
                        updated_at = clock_timestamp()
                    WHERE run.status IN ('queued', 'running')
                      AND run.updated_at <= clock_timestamp() - make_interval(secs => p_stale_seconds)
                    RETURNING run.id::text, run.user_id::text, run.session_id::text;
                END
                $stale$;

                CREATE OR REPLACE FUNCTION fail_running_tool_executions(p_reason text)
                RETURNS TABLE(execution_id text, user_id text, session_id text, project_id text)
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = public, pg_temp
                SET row_security = off
                AS $tools$
                BEGIN
                    IF NULLIF(current_setting('app.current_user_id', true), '') IS NOT NULL THEN
                        RAISE EXCEPTION 'fail_running_tool_executions is restricted to background connections without tenant scope';
                    END IF;
                    IF p_reason IS NULL OR btrim(p_reason) = '' THEN
                        RAISE EXCEPTION 'failure reason is required';
                    END IF;

                    RETURN QUERY
                    UPDATE agent_tool_executions AS execution
                    SET status = 'failed',
                        result_phase = 'runtime_cancelled',
                        result_message = btrim(p_reason),
                        error_type = 'runtime_cancelled',
                        error_message = btrim(p_reason),
                        failure_json = jsonb_build_object(
                            'code', 'RUNTIME_CANCELLED',
                            'failedStage', 'runtime_cancelled',
                            'reason', btrim(p_reason),
                            'recoverable', true,
                            'recommendedAction', 'QueryRuntimeRun')::text,
                        completed_at = clock_timestamp(),
                        duration_ms = GREATEST(
                            0,
                            floor(extract(epoch FROM (clock_timestamp() - execution.started_at)) * 1000)::integer)
                    WHERE execution.status = 'running'
                    RETURNING
                        execution.id::text,
                        execution.user_id::text,
                        execution.session_id::text,
                        execution.project_id::text;
                END
                $tools$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP FUNCTION IF EXISTS fail_running_tool_executions(text);
                DROP FUNCTION IF EXISTS fail_stale_runtime_runs(integer, text);
                DROP FUNCTION IF EXISTS claim_runtime_run(text);
                DROP FUNCTION IF EXISTS list_queued_runtime_runs(integer);
                DROP FUNCTION IF EXISTS list_active_runtime_sessions(integer);
                DROP FUNCTION IF EXISTS claim_outbox_events(text, integer, integer);
                """);

            migrationBuilder.DropIndex(
                name: "ux_model_executions_idempotency",
                table: "model_executions");

            migrationBuilder.DropColumn(
                name: "idempotency_key",
                table: "model_executions");

            migrationBuilder.DropColumn(
                name: "result_json",
                table: "model_executions");
        }
    }
}
