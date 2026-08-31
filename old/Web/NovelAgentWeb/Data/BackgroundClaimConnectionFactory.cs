using Npgsql;

namespace TM.Web.NovelAgentWeb.Data;

public interface IBackgroundClaimConnectionFactory
{
    Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken = default);
}

public interface IBackgroundClaimDatabasePreflight
{
    Task VerifyRoleAsync(CancellationToken cancellationToken = default);
    Task VerifyPermissionsAsync(CancellationToken cancellationToken = default);
}

public sealed class BackgroundClaimConnectionFactory : IBackgroundClaimConnectionFactory
{
    private readonly string _connectionString;

    public BackgroundClaimConnectionFactory(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("NovelAgentWorkerDb")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:NovelAgentWorkerDb 未配置，后台 claim 不得复用 HTTP 应用数据库角色。");
        var connection = new NpgsqlConnectionStringBuilder(_connectionString);
        if (!string.Equals(connection.Username, "novelagent_worker", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:NovelAgentWorkerDb 必须使用独立角色 novelagent_worker。");
        }
        if (string.IsNullOrWhiteSpace(connection.Password))
            throw new InvalidOperationException("novelagent_worker 数据库密码未配置。");
    }

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new NpgsqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}

public sealed class BackgroundClaimDatabasePreflight : IBackgroundClaimDatabasePreflight
{
    private readonly IBackgroundClaimConnectionFactory _connections;

    public BackgroundClaimDatabasePreflight(IBackgroundClaimConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task VerifyRoleAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT current_user";
            var currentUser = (string?)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (!string.Equals(currentUser, "novelagent_worker", StringComparison.Ordinal))
                throw new InvalidOperationException($"后台数据库连接实际角色为 {currentUser ?? "<null>"}。");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                "novelagent_worker 角色登录预检失败。请先执行 PostgreSQL role bootstrap，再启动 API。",
                exception);
        }
    }

    public async Task VerifyPermissionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await _connections.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT current_user = 'novelagent_worker'
                   AND has_function_privilege(current_user, 'claim_kernel_task(text,integer)', 'EXECUTE')
                   AND has_function_privilege(current_user, 'claim_knowledge_processing_task(text,integer)', 'EXECUTE')
                   AND has_function_privilege(current_user, 'claim_stale_model_execution(integer)', 'EXECUTE')
                   AND has_function_privilege(current_user, 'settle_next_model_execution()', 'EXECUTE')
                   AND has_function_privilege(current_user, 'claim_outbox_events(text,integer,integer)', 'EXECUTE')
                   AND has_function_privilege(current_user, 'list_active_runtime_sessions(integer)', 'EXECUTE')
                   AND has_function_privilege(current_user, 'list_active_runtime_session_page(integer,timestamp with time zone,text)', 'EXECUTE')
                   AND has_function_privilege(current_user, 'list_queued_runtime_runs(integer)', 'EXECUTE')
                   AND has_function_privilege(current_user, 'claim_runtime_run(text)', 'EXECUTE')
                   AND has_function_privilege(current_user, 'fail_stale_runtime_runs(integer,text)', 'EXECUTE')
                   AND has_function_privilege(current_user, 'fail_running_tool_executions(text)', 'EXECUTE')
                   AND has_function_privilege(current_user, 'fail_stale_tool_executions(integer,text)', 'EXECUTE')
                """;
            var valid = (bool?)await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (valid != true)
                throw new InvalidOperationException("novelagent_worker 缺少一个或多个后台函数的 EXECUTE 权限。");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                "novelagent_worker 权限预检失败。请确认目标架构迁移已完成且 worker grants 已应用。",
                exception);
        }
    }
}
