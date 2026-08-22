using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Tianming.NovelAgent.Application.Ports;

namespace Tianming.NovelAgent.Infrastructure.Persistence;

public sealed class AgentUserScope : IUserScope
{
    private readonly AsyncLocal<string?> _current = new();

    public string? CurrentUserId => _current.Value;

    public IDisposable Enter(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("A user scope requires a user id.", nameof(userId));

        var previous = _current.Value;
        _current.Value = userId.Trim();
        return new Scope(() => _current.Value = previous);
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}

public sealed class AgentUserScopeConnectionInterceptor(IUserScope userScope) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        SetScopeAsync(connection, CancellationToken.None).GetAwaiter().GetResult();

    public override Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default) =>
        SetScopeAsync(connection, cancellationToken);

    private async Task SetScopeAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection is not NpgsqlConnection)
            return;

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('app.current_user_id', @user_id, false)";
        command.Parameters.Add(new NpgsqlParameter("user_id", userScope.CurrentUserId ?? string.Empty));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
